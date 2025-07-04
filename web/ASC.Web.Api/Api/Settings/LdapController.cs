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

namespace ASC.Web.Api.Controllers.Settings;

[ApiExplorerSettings(IgnoreApi = true)]
[DefaultRoute("ldap")]
public class LdapController(
    ApiContext apiContext,
    WebItemManager webItemManager,
    IFusionCache fusionCache,
    SettingsManager settingsManager,
    TenantManager tenantManager,
    LdapNotifyService ldapNotifyHelper,
    LdapSaveSyncOperation ldapSaveSyncOperation,
    AuthContext authContext,
    PermissionContext permissionContext,
    CoreBaseSettings coreBaseSettings,
    IHttpContextAccessor httpContextAccessor,
    IMapper mapper)
    : BaseSettingsController(apiContext, fusionCache, webItemManager, httpContextAccessor)
{
    /// <summary>
    /// Returns the current portal LDAP settings.
    /// </summary>
    /// <short>
    /// Get the LDAP settings
    /// </short>
    /// <path>api/2.0/settings/ldap</path>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Settings / LDAP")]
    [SwaggerResponse(200, "LDAP settings", typeof(LdapSettingsDto))]
    [HttpGet("")]
    public async Task<LdapSettingsDto> GetLdapSettingsAsync()
    {
        await CheckLdapPermissionsAsync();

        var settings = await settingsManager.LoadAsync<LdapSettings>();

        settings = settings.Clone() as LdapSettings; // clone LdapSettings object for clear password (potencial AscCache.Memory issue)

        if (settings == null)
        {
            settings = new LdapSettings().GetDefault();
            return mapper.Map<LdapSettings, LdapSettingsDto>(settings);
        }

        settings.Password = null;
        settings.PasswordBytes = null;

        if (settings.IsDefault)
        {
            return mapper.Map<LdapSettings, LdapSettingsDto>(settings);
        }

        var defaultSettings = settings.GetDefault();

        if (settings.Equals(defaultSettings))
        {
            settings.IsDefault = true;
        }

        return mapper.Map<LdapSettings, LdapSettingsDto>(settings);
    }

    /// <summary>
    /// Returns the LDAP asynchronous cron expression for the current portal if it exists.
    /// </summary>
    /// <short>
    /// Get the LDAP cron expression
    /// </short>
    /// <path>api/2.0/settings/ldap/cron</path>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Settings / LDAP")]
    [SwaggerResponse(200, "LDAP cron settings", typeof(LdapCronSettingsDto))]
    [HttpGet("cron")]
    public async Task<LdapCronSettingsDto> GetLdapCronSettingsAsync()
    {
        await CheckLdapPermissionsAsync();

        var settings = await settingsManager.LoadAsync<LdapCronSettings>() ?? new LdapCronSettings().GetDefault();

        if (string.IsNullOrEmpty(settings.Cron))
        {
            return null;
        }

        return mapper.Map<LdapCronSettings, LdapCronSettingsDto>(settings);
    }

    /// <summary>
    /// Sets the LDAP asynchronous cron expression to the current portal.
    /// </summary>
    /// <short>
    /// Set the LDAP cron expression
    /// </short>
    /// <path>api/2.0/settings/ldap/cron</path>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Settings / LDAP")]
    [HttpPost("cron")]
    public async Task SetLdapCronSettingsAsync(LdapCronRequestDto inDto)
    {
        await CheckLdapPermissionsAsync();

        var cron = inDto?.Cron;

        if (!string.IsNullOrEmpty(cron))
        {
            new CronExpression(cron); // validate

            if (!(await settingsManager.LoadAsync<LdapSettings>()).EnableLdapAuthentication)
            {
                throw new Exception(Resource.LdapSettingsErrorCantSaveLdapSettings);
            }
        }

        var settings = await settingsManager.LoadAsync<LdapCronSettings>() ?? new LdapCronSettings();

        settings.Cron = cron;
        await settingsManager.SaveAsync(settings);

        var t = tenantManager.GetCurrentTenant();
        if (!string.IsNullOrEmpty(cron))
        {
            ldapNotifyHelper.UnregisterAutoSync(t);
            ldapNotifyHelper.RegisterAutoSync(t, cron);
        }
        else
        {
            ldapNotifyHelper.UnregisterAutoSync(t);
        }
    }

    /// <summary>
    /// Synchronizes the portal data with the new information from the LDAP server.
    /// </summary>
    /// <short>
    /// Synchronize with LDAP server
    /// </short>
    /// <path>api/2.0/settings/ldap/sync</path>
    [Tags("Settings / LDAP")]
    [SwaggerResponse(200, "LDAP operation status", typeof(LdapStatusDto))]
    [HttpGet("sync")]
    public async Task<LdapStatusDto> SyncLdapAsync()
    {
        await CheckLdapPermissionsAsync();

        var ldapSettings = await settingsManager.LoadAsync<LdapSettings>();

        var userId = authContext.CurrentAccount.ID.ToString();

        var tenant = tenantManager.GetCurrentTenant();
        
        var result = await ldapSaveSyncOperation.SyncLdapAsync(ldapSettings, tenant, userId);

        return mapper.Map<LdapOperationStatus, LdapStatusDto>(result);
    }

    /// <summary>
    /// Starts the process of collecting preliminary changes on the portal during the synchronization process according to the selected LDAP settings.
    /// </summary>
    /// <short>
    /// Test the LDAP synchronization
    /// </short>
    /// <path>api/2.0/settings/ldap/sync/test</path>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Settings / LDAP")]
    [SwaggerResponse(200, "LDAP operation status", typeof(LdapStatusDto))]
    [HttpGet("sync/test")]
    public async Task<LdapStatusDto> TestLdapSync()
    {
        await CheckLdapPermissionsAsync();

        var ldapSettings = await settingsManager.LoadAsync<LdapSettings>();

        var tenant = tenantManager.GetCurrentTenant();
        
        var result = await ldapSaveSyncOperation.TestLdapSyncAsync(ldapSettings, tenant);

        return mapper.Map<LdapOperationStatus, LdapStatusDto>(result);
    }

    /// <summary>
    /// Saves the LDAP settings specified in the request and starts importing/synchronizing users and groups by LDAP.
    /// </summary>
    /// <short>
    /// Save the LDAP settings
    /// </short>
    /// <path>api/2.0/settings/ldap</path>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Settings / LDAP")]
    [SwaggerResponse(200, "LDAP operation status", typeof(LdapStatusDto))]
    [HttpPost("")]
    public async Task<LdapStatusDto> SaveLdapSettingsAsync(LdapRequestsDto inDto)
    {
        var ldapSettings = mapper.Map<LdapRequestsDto, LdapSettings>(inDto);

        await CheckLdapPermissionsAsync();

        if (!ldapSettings.EnableLdapAuthentication)
        {
            await SetLdapCronSettingsAsync(null);
        }

        var userId = authContext.CurrentAccount.ID.ToString();

        var tenant = tenantManager.GetCurrentTenant();
        
        var result = await ldapSaveSyncOperation.SaveLdapSettingsAsync(ldapSettings, tenant, userId);

        return mapper.Map<LdapOperationStatus, LdapStatusDto>(result);
    }

    /// <summary>
    /// Starts the process of saving LDAP settings and collecting preliminary changes on the portal according to them.
    /// </summary>
    /// <short>
    /// Test the LDAP saving process
    /// </short>
    /// <path>api/2.0/settings/ldap/save/test</path>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Settings / LDAP")]
    [SwaggerResponse(200, "LDAP operation status", typeof(LdapStatusDto))]
    [HttpPost("save/test")]
    public async Task<LdapStatusDto> TestLdapSaveAsync(LdapSettings inDto)
    {
        await CheckLdapPermissionsAsync();

        var userId = authContext.CurrentAccount.ID.ToString();

        var tenant = tenantManager.GetCurrentTenant();
        
        var result = await ldapSaveSyncOperation.TestLdapSaveAsync(inDto, tenant, userId);

        return mapper.Map<LdapOperationStatus, LdapStatusDto>(result);
    }

    /// <summary>
    /// Returns the LDAP synchronization process status.
    /// </summary>
    /// <short>
    /// Get the LDAP synchronization status
    /// </short>
    /// <path>api/2.0/settings/ldap/status</path>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Settings / LDAP")]
    [SwaggerResponse(200, "LDAP operation status", typeof(LdapStatusDto))]
    [HttpGet("status")]
    public async Task<LdapStatusDto> GetLdapOperationStatusAsync()
    {
        await CheckLdapPermissionsAsync();

        var tenant = tenantManager.GetCurrentTenant();
        
        var result = await ldapSaveSyncOperation.ToLdapOperationStatus(tenant.Id);

        return mapper.Map<LdapOperationStatus, LdapStatusDto>(result);
    }

    /// <summary>
    /// Returns the LDAP default settings.
    /// </summary>
    /// <short>
    /// Get the LDAP default settings
    /// </short>
    /// <path>api/2.0/settings/ldap/default</path>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Settings / LDAP")]
    [SwaggerResponse(200, "LDAP default settings: enable LDAP authentication or not, start TLS or not, enable SSL or not, send welcome email or not, server name, user name, port number, user filter, login attribute, LDAP settings mapping, access rights, user is a group member or not, group name, user attribute, group filter, group attribute, group name attribute, authentication is enabled or not, login, password, accept certificate or not", typeof(LdapSettingsDto))]
    [HttpGet("default")]
    public async Task<LdapSettingsDto> GetDefaultLdapSettingsAsync()
    {
        await CheckLdapPermissionsAsync();

        var settings = new LdapSettings().GetDefault();

        return mapper.Map<LdapSettings, LdapSettingsDto>(settings);
    }

    private async Task CheckLdapPermissionsAsync()
    {
        await permissionContext.DemandPermissionsAsync(SecurityConstants.EditPortalSettings);

        if (!coreBaseSettings.Standalone
            && (!SetupInfo.IsVisibleSettings(ManagementType.LdapSettings.ToStringFast())
                || !(await tenantManager.GetCurrentTenantQuotaAsync()).Ldap))
        {
            throw new BillingException(Resource.ErrorNotAllowedOption);
        }
    }
}
