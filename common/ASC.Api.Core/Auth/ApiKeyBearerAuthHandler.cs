// (c) Copyright Ascensio System SIA 2009-2024
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

using SecurityContext = ASC.Core.SecurityContext;

namespace ASC.Api.Core.Auth;

[Scope]
public class ApiKeyBearerAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyManager apiKeyManager,
    SecurityContext securityContext
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(token))
        {
            throw new AuthenticationException(nameof(HttpStatusCode.Unauthorized));
        }

        token = token.Trim();

        if (0 <= token.IndexOf("Bearer", 0, StringComparison.Ordinal))
        {
            token = token["Bearer ".Length..];
        }
        
        var apiKey = await apiKeyManager.ValidateApiKeyAsync(token);
        
        try
        {
            if (apiKey == null)
            {
                throw new AuthenticationException("Api key is invalid");
            }
            
            var claims = new List<Claim>
            {
                AuthConstants.Claim_ScopeRootWrite
            };

            if (apiKey.Permissions is { Count: > 0 } && !apiKey.Permissions.Exists(x=> x == "*"))
            {
                claims = apiKey.Permissions.ConvertAll(x => new Claim("scope", x));
            }
            
            await securityContext.AuthenticateMeWithoutCookieAsync(apiKey.CreateBy, claims);
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail(new AuthenticationException(nameof(HttpStatusCode.Unauthorized), ex));
        }

        return AuthenticateResult.Success(new AuthenticationTicket(Context.User, Scheme.Name));
    }
}

public static class ApiKeyBearerDefaults
{
    public const string AuthenticationScheme = "ApiKeyBearer";
}