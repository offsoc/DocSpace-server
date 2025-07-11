﻿// (c) Copyright Ascensio System SIA 2009-2025
// 
// This program is a free software product.
// You can redistribute it and/or modify it under the terms
// of the GNU Affero General Public License (AGPL) version 3 as published by the Free Software
// Foundation. In accordance with Section 7(a) of the GNU AGPL its Section 15 shall be amended
// to the effect that Ascensio System SIA expressly excludes the warranty of non-infringement of
// any third-party rights.
// 
// This program is distributed WITHOUT ANY WARRANTY, without even the implied warranty
// of MERCHANTABILITY or FITNESS FOR A PARTICULAR  PURPOSE. For details, see
// the GNU AGPL at: http://www.gnu.org/licenses/agpl-3.0.html
// 
// You can contact Ascensio System SIA at Lubanas st. 125a-25, Riga, Latvia, EU, LV-1021.
// 
// The  interactive user interfaces in modified source and object code versions of the Program must
// display Appropriate Legal Notices, as required under Section 5 of the GNU AGPL version 3.
// 
// Pursuant to Section 7(b) of the License you must retain the original Product logo when
// distributing the program. Pursuant to Section 7(e) we decline to grant you any rights under
// trademark law for use of our trademarks.
// 
// All the Product's GUI elements, including illustrations and icon sets, as well as technical writing
// content are licensed under the terms of the Creative Commons Attribution-ShareAlike 4.0
// International. See the License terms at http://creativecommons.org/licenses/by-sa/4.0/legalcode

namespace ASC.Web.Api;

/// <summary>
/// Security API.
/// </summary>
/// <name>security</name>
[Scope]
[DefaultRoute("activeconnections")]
[ApiController]
[ControllerName("security")]
public class ConnectionsController(
    UserManager userManager,
    SecurityContext securityContext,
    DbLoginEventsManager dbLoginEventsManager,
    IHttpContextAccessor httpContextAccessor,
    DisplayUserSettingsHelper displayUserSettingsHelper,
    CommonLinkUtility commonLinkUtility,
    ILogger<ConnectionsController> logger,
    WebItemSecurity webItemSecurity,
    MessageService messageService,
    CookiesManager cookiesManager,
    CookieStorage cookieStorage,
    QuotaSocketManager quotaSocketManager,
    GeolocationHelper geolocationHelper,
    ApiDateTimeHelper apiDateTimeHelper)
    : ControllerBase
{
    /// <summary>
    /// Returns all the active connections to the portal.
    /// </summary>
    /// <short>
    /// Get active connections
    /// </short>
    /// <path>api/2.0/security/activeconnections</path>
    [Tags("Security / Active connections")]
    [SwaggerResponse(200, "Active portal connections", typeof(ActiveConnectionsDto))]
    [HttpGet("")]
    public async Task<ActiveConnectionsDto> GetAllActiveConnections()
    {
        var user = await userManager.GetUsersAsync(securityContext.CurrentAccount.ID);
        var loginEvents = await dbLoginEventsManager.GetLoginEventsAsync(user.TenantId, user.Id);
        var tasks = loginEvents.ConvertAll(async r => await geolocationHelper.AddGeolocationAsync(r));
        var listLoginEvents = (await Task.WhenAll(tasks)).ToList();
        var loginEventId = GetLoginEventIdFromCookie();
        if (loginEventId != 0)
        {
            var loginEvent = listLoginEvents.Find(x => x.Id == loginEventId);
            if (loginEvent != null)
            {
                listLoginEvents.Remove(loginEvent);

                if (httpContextAccessor.HttpContext != null)
                {
                    var baseEvent = await GetBaseEvent();

                    loginEvent.Platform = baseEvent.Platform;
                    loginEvent.Browser = baseEvent.Browser;
                    loginEvent.IP = baseEvent.IP;
                    loginEvent.City = baseEvent.City;
                    loginEvent.Country = baseEvent.Country;
                }

                listLoginEvents.Insert(0, loginEvent);
            }
        }
        else
        {
            if (listLoginEvents.Count == 0 && httpContextAccessor.HttpContext != null)
            {
                var baseEvent = await GetBaseEvent();

                listLoginEvents.Add(baseEvent);
            }
        }

        return new ActiveConnectionsDto
        {
            LoginEvent = loginEventId,
            Items = listLoginEvents.Select(q => new ActiveConnectionsItemDto
            {
                Id = q.Id,
                Browser = q.Browser,
                City = q.City,
                Country = q.Country,
                Date = apiDateTimeHelper.Get(q.Date),
                Ip = q.IP,
                Mobile = q.Mobile,
                Page = q.Page,
                TenantId = q.TenantId,
                Platform = q.Platform,
                UserId = q.UserId

            }).ToList()
        };

        async Task<BaseEvent> GetBaseEvent()
        {
            var request = Request;
            var uaHeader = MessageSettings.GetUAHeader(request);
            var clientInfo = MessageSettings.GetClientInfo(uaHeader);
            var platformAndDevice = MessageSettings.GetPlatformAndDevice(clientInfo);
            var browser = MessageSettings.GetBrowser(clientInfo);
            var ip = MessageSettings.GetIP(request);

            var baseEvent = new BaseEvent
            {
                Platform = platformAndDevice,
                Browser = browser,
                Date = DateTime.Now,
                IP = ip
            };

            return await geolocationHelper.AddGeolocationAsync(baseEvent);
        }
    }

    /// <summary>
    /// Logs out from all the active connections for the current user and changes their password.
    /// </summary>
    /// <short>
    /// Log out and change password
    /// </short>
    /// <path>api/2.0/security/activeconnections/logoutallchangepassword</path>
    [Tags("Security / Active connections")]
    [SwaggerResponse(200, "URL to the confirmation message for changing a password", typeof(string))]
    [HttpPut("logoutallchangepassword")]
    public async Task<string> LogOutAllActiveConnectionsChangePassword()
    {
        try
        {
            var user = await userManager.GetUsersAsync(securityContext.CurrentAccount.ID);
            var userName = user.DisplayUserName(false, displayUserSettingsHelper);

            await LogOutAllActiveConnections(user.Id);

            securityContext.Logout();

            var auditEventDate = DateTime.UtcNow;
            auditEventDate = auditEventDate.AddTicks(-(auditEventDate.Ticks % TimeSpan.TicksPerSecond));

            var hash = auditEventDate.ToString("s", CultureInfo.InvariantCulture);
            var confirmationUrl = commonLinkUtility.GetConfirmationEmailUrl(user.Email, ConfirmType.PasswordChange, hash, user.Id);

            messageService.Send(MessageAction.UserSentPasswordChangeInstructions, MessageTarget.Create(user.Id), auditEventDate, userName);

            return confirmationUrl;
        }
        catch (Exception ex)
        {
            logger.ErrorWithException(ex);
            return null;
        }
    }

    /// <summary>
    /// Logs out from all the active connections for the user with the ID specified in the request.
    /// </summary>
    /// <short>
    /// Log out for the user by ID
    /// </short>
    /// <path>api/2.0/security/activeconnections/logoutall/{userId}</path>
    [Tags("Security / Active connections")]
    [SwaggerResponse(200, "Ok")]
    [SwaggerResponse(403, "Method not available")]
    [HttpPut("logoutall/{userId:guid}")]
    public async Task LogOutAllActiveConnectionsForUserAsync(UserIdRequestDto inDto)
    {
        var currentUserId = securityContext.CurrentAccount.ID;
        if (!await userManager.IsDocSpaceAdminAsync(currentUserId) && 
            !await webItemSecurity.IsProductAdministratorAsync(WebItemManager.PeopleProductID, currentUserId) || 
            (currentUserId != inDto.Id && await userManager.IsDocSpaceAdminAsync(inDto.Id)))
        {
            throw new SecurityException("Method not available");
        }

        await LogOutAllActiveConnections(inDto.Id);
    }

    /// <summary>
    /// Logs out from all the active connections except the current connection.
    /// </summary>
    /// <short>
    /// Log out from all connections except the current one
    /// </short>
    /// <path>api/2.0/security/activeconnections/logoutallexceptthis</path>
    [Tags("Security / Active connections")]
    [SwaggerResponse(200, "Current user name", typeof(string))]
    [HttpPut("logoutallexceptthis")]
    public async Task<string> LogOutAllExceptThisConnection()
    {
        try
        {
            var user = await userManager.GetUsersAsync(securityContext.CurrentAccount.ID);
            var userName = user.DisplayUserName(false, displayUserSettingsHelper);
            var loginEventFromCookie = GetLoginEventIdFromCookie();

            var loginEvents = await dbLoginEventsManager.LogOutAllActiveConnectionsExceptThisAsync(loginEventFromCookie, user.TenantId, user.Id);

            foreach (var loginEvent in loginEvents)
            {
                await quotaSocketManager.LogoutSession(user.Id, loginEvent.Id);
            }

            messageService.Send(MessageAction.UserLogoutActiveConnections, userName);
            return userName;
        }
        catch (Exception ex)
        {
            logger.ErrorWithException(ex);
            return null;
        }
    }

    /// <summary>
    /// Logs out from the connection with the ID specified in the request.
    /// </summary>
    /// <short>
    /// Log out from the connection
    /// </short>
    /// <path>api/2.0/security/activeconnections/logout/{loginEventId}</path>
    [Tags("Security / Active connections")]
    [SwaggerResponse(200, "Boolean value: true if the operation is successful", typeof(bool))]
    [SwaggerResponse(403, "Method not available")]
    [HttpPut("logout/{loginEventId:int}")]
    public async Task<bool> LogOutActiveConnection(LoginEvenrIdRequestDto inDto)
    {
        try
        {
            var currentUserId = securityContext.CurrentAccount.ID;
            var user = await userManager.GetUsersAsync(currentUserId);

            var loginEvent = await dbLoginEventsManager.GetByIdAsync(user.TenantId, inDto.Id);

            if (loginEvent == null)
            {
                return false;
            }

            if (loginEvent.UserId.HasValue && currentUserId != loginEvent.UserId && !await userManager.IsDocSpaceAdminAsync(user))
            {
                throw new SecurityException("Method not available");
            }

            var userName = user.DisplayUserName(false, displayUserSettingsHelper);

            await dbLoginEventsManager.LogOutEventAsync(loginEvent.TenantId, loginEvent.Id);

            if (loginEvent.UserId.HasValue)
            {
                await quotaSocketManager.LogoutSession(loginEvent.UserId.Value, loginEvent.Id);
            }

            messageService.Send(MessageAction.UserLogoutActiveConnection, userName);
            return true;
        }
        catch (Exception ex)
        {
            logger.ErrorWithException(ex);
            return false;
        }
    }

    private async Task LogOutAllActiveConnections(Guid? userId = null)
    {
        var currentUserId = securityContext.CurrentAccount.ID;
        var user = await userManager.GetUsersAsync(userId ?? currentUserId);
        var userName = user.DisplayUserName(false, displayUserSettingsHelper);
        var auditEventDate = DateTime.UtcNow;

        messageService.Send(currentUserId.Equals(user.Id) ? MessageAction.UserLogoutActiveConnections : MessageAction.UserLogoutActiveConnectionsForUser, MessageTarget.Create(user.Id), auditEventDate, userName);
        await cookiesManager.ResetUserCookieAsync(user.Id);

        await quotaSocketManager.LogoutSession(user.Id);
    }

    private int GetLoginEventIdFromCookie()
    {
        var cookie = cookiesManager.GetCookies(CookiesType.AuthKey);
        var loginEventId = cookieStorage.GetLoginEventIdFromCookie(cookie);
        return loginEventId;
    }
}
