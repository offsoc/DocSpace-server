// (c) Copyright Ascensio System SIA 2009-2025
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

namespace ASC.Data.Storage;

public static class SecureHelper
{
    public static bool IsSecure(HttpContext httpContext, ILoggerProvider options)
    {
        try
        {
            return httpContext != null && Uri.UriSchemeHttps.Equals(httpContext.Request.Url().Scheme, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception err)
        {
            options.CreateLogger("ASC.Data.Storage.SecureHelper").ErrorIsSecure(err);

            return false;
        }
    }

    public static string GenerateSecureKeyHeader(string path, EmailValidationKeyProvider keyProvider)
    {
        var ticks = DateTime.UtcNow.Ticks;
        var data = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar) + '.' + ticks;
        var key = keyProvider.GetEmailKey(data);

        return Constants.SecureKeyHeader + ':' + ticks + '-' + key;
    }

    public static bool CheckSecureKeyHeader(string queryHeaders, string path, EmailValidationKeyProvider keyProvider)
    {
        if (string.IsNullOrEmpty(queryHeaders))
        {
            return false;
        }
        
        var headers = queryHeaders.Length > 0 ? queryHeaders.Split('&').Select(HttpUtility.UrlDecode) : [];

        var headerKey = headers.FirstOrDefault(h => h.StartsWith(Constants.SecureKeyHeader))?.
            Replace(Constants.SecureKeyHeader + ':', string.Empty);

        if (string.IsNullOrEmpty(headerKey))
        {
            return false;
        }

        var separatorPosition = headerKey.IndexOf('-');
        var ticks = headerKey[..separatorPosition];
        var key = headerKey[(separatorPosition + 1)..];

        var result = keyProvider.ValidateEmailKey(path + '.' + ticks, key);

        return result == EmailValidationKeyProvider.ValidationResult.Ok;
    }
}
