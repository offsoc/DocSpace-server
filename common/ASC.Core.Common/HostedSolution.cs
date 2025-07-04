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

using Constants = ASC.Core.Users.Constants;

namespace ASC.Core;


[Scope]
public class HostedSolution(ITenantService tenantService,
    IUserService userService,
    IQuotaService quotaService,
    ITariffService tariffService,
    UserFormatter userFormatter,
    TenantManager clientTenantManager,
    TenantUtil tenantUtil,
    SettingsManager settingsManager,
    CoreSettings coreSettings)
{
    public async Task<List<Tenant>> GetTenantsAsync(DateTime from)
    {
        return (await tenantService.GetTenantsAsync(from)).ToList();
    }

    public async Task<List<Tenant>> FindTenantsAsync(string login, string passwordHash = null)
    {
        if (!string.IsNullOrEmpty(passwordHash) && await userService.GetUserByPasswordHashAsync(Tenant.DefaultTenant, login, passwordHash) == null)
        {
            throw new SecurityException("Invalid login or password.");
        }

        return (await tenantService.GetTenantsAsync(login, passwordHash)).ToList();
    }

    public async Task<Tenant> GetTenantAsync(string domain)
    {
        return await tenantService.GetTenantAsync(domain);
    }

    public async Task<Tenant> GetTenantAsync(int id)
    {
        return await tenantService.GetTenantAsync(id);
    }

    public async Task CheckTenantAddressAsync(string address)
    {
        await tenantService.ValidateDomainAsync(address);
    }

    public async Task<bool> IsForbiddenDomainAsync(string domain)
    {
        return await tenantService.IsForbiddenDomainAsync(domain);
    }

    public async Task<Tenant> RegisterTenantAsync(TenantRegistrationInfo registrationInfo)
    {
        ArgumentNullException.ThrowIfNull(registrationInfo);

        if (string.IsNullOrEmpty(registrationInfo.Address))
        {
            throw new Exception("Address can not be empty");
        }
        if (string.IsNullOrEmpty(registrationInfo.Email))
        {
            throw new Exception("Account email can not be empty");
        }
        if (registrationInfo.FirstName == null)
        {
            throw new Exception("Account firstname can not be empty");
        }
        if (registrationInfo.LastName == null)
        {
            throw new Exception("Account lastname can not be empty");
        }
        if (!userFormatter.IsValidUserName(registrationInfo.FirstName, registrationInfo.LastName))
        {
            throw new Exception("Incorrect firstname or lastname");
        }

        if (string.IsNullOrEmpty(registrationInfo.PasswordHash))
        {
            registrationInfo.PasswordHash = Guid.NewGuid().ToString();
        }

        tenantService.ValidateTenantName(registrationInfo.Name);

        // create tenant
        var tenant = new Tenant(registrationInfo.Address.ToLowerInvariant())
        {
            Name = registrationInfo.Name,
            Language = registrationInfo.Culture.Name,
            TimeZone = registrationInfo.TimeZoneInfo.Id,
            HostedRegion = registrationInfo.HostedRegion,
            PartnerId = registrationInfo.PartnerId,
            AffiliateId = registrationInfo.AffiliateId,
            Campaign = registrationInfo.Campaign,
            Industry = registrationInfo.Industry,
            Calls = registrationInfo.Calls
        };

        tenant = await tenantService.SaveTenantAsync(coreSettings, tenant);

        // create user
        var user = new UserInfo
        {
            UserName = registrationInfo.Email[..registrationInfo.Email.IndexOf('@')],
            LastName = registrationInfo.LastName,
            FirstName = registrationInfo.FirstName,
            Email = registrationInfo.Email,
            MobilePhone = registrationInfo.MobilePhone,
            WorkFromDate = tenantUtil.DateTimeNow(tenant.TimeZone),
            ActivationStatus = registrationInfo.ActivationStatus,
            Spam = registrationInfo.Spam
        };

        user = await userService.SaveUserAsync(tenant.Id, user);
        await userService.SetUserPasswordHashAsync(tenant.Id, user.Id, registrationInfo.PasswordHash);
        await userService.SaveUserGroupRefAsync(tenant.Id, new UserGroupRef(user.Id, Constants.GroupAdmin.ID, UserGroupRefType.Contains));

        // save tenant owner
        tenant.OwnerId = user.Id;

        await tenantService.SaveTenantAsync(coreSettings, tenant);

        await settingsManager.SaveAsync(new TenantAccessSpaceSettings { LimitedAccessSpace = registrationInfo.LimitedAccessSpace }, tenant.Id);

        return tenant;
    }

    public async Task<Tenant> SaveTenantAsync(Tenant tenant)
    {
        return await tenantService.SaveTenantAsync(coreSettings, tenant);
    }

    public async Task RemoveTenantAsync(Tenant tenant)
    {
        await tenantService.RemoveTenantAsync(tenant);
    }

    public async Task<string> CreateAuthenticationCookieAsync(CookieStorage cookieStorage, int tenantId, Guid userId)
    {
        var u = await userService.GetUserAsync(tenantId, userId);

        return await CreateAuthenticationCookieAsync(cookieStorage, tenantId, u);
    }

    private async Task<string> CreateAuthenticationCookieAsync(CookieStorage cookieStorage, int tenantId, UserInfo user)
    {
        if (user == null)
        {
            return null;
        }

        var tenantSettings = await settingsManager.LoadAsync<TenantCookieSettings>(tenantId, Guid.Empty);
        var expires = tenantSettings.IsDefault() ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMinutes(tenantSettings.LifeTime);
        var userSettings = await settingsManager.LoadAsync<TenantCookieSettings>(tenantId, user.Id);

        return await cookieStorage.EncryptCookieAsync(tenantId, user.Id, tenantSettings.Index, expires, userSettings.Index, 0);
    }

    public async Task<Tariff> GetTariffAsync(int tenant, bool withRequestToPaymentSystem = true)
    {
        return await tariffService.GetTariffAsync(tenant, withRequestToPaymentSystem);
    }

    public async Task<TenantQuotaSettings> GetTenantQuotaSettings(int tenantId)
    {
        return await settingsManager.LoadAsync<TenantQuotaSettings>(tenantId, Guid.Empty);
    }

    public async Task<TenantQuota> GetTenantQuotaAsync(int tenant)
    {
        return await clientTenantManager.GetTenantQuotaAsync(tenant);
    }

    public async Task<List<TenantQuotaRow>> FindTenantQuotaRowsAsync(int tenant)
    {
        return await clientTenantManager.FindTenantQuotaRowsAsync(tenant);
    }

    public async Task<IEnumerable<TenantQuota>> GetTenantQuotasAsync()
    {
        return await clientTenantManager.GetTenantQuotasAsync();
    }

    public async Task<TenantQuota> SaveTenantQuotaAsync(TenantQuota quota)
    {
        return await clientTenantManager.SaveTenantQuotaAsync(quota);
    }

    public async Task SetTariffAsync(int tenant, bool paid)
    {
        var quota = (await quotaService.GetTenantQuotasAsync()).FirstOrDefault(q => paid ? q.NonProfit : q.Trial);
        if (quota != null)
        {
            await tariffService.SetTariffAsync(tenant, new Tariff { Quotas = [new Quota(quota.TenantId, 1)], DueDate = DateTime.MaxValue });
        }
    }

    public async Task SetTariffAsync(int tenant, Tariff tariff)
    {
        await tariffService.SetTariffAsync(tenant, tariff);
    }

    public async Task<IEnumerable<UserInfo>> FindUsersAsync(IEnumerable<Guid> userIds)
    {
        return await userService.GetUsersAllTenantsAsync(userIds);
    }
}
