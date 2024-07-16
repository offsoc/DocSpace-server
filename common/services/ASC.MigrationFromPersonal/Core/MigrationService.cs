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

using System.Diagnostics;

using ASC.Core;
using ASC.Web.Studio.Core.Notify;

namespace ASC.MigrationFromPersonal;

[Singleton]
public class MigrationService(IServiceProvider serviceProvider,
    IConfiguration configuration,
    IDbContextFactory<MigrationContext> dbContextFactory,
    ILogger<MigrationService> logger,
    CreatorDbContext creatorDbContext) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        while (!stoppingToken.IsCancellationRequested)
        {
            var email = await context.Migrations.OrderBy(m => m.RequestDate)
                .Where(m => m.Status == MigrationStatus.Pending).Select(m => m.Email).FirstOrDefaultAsync();
            if (email == null)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                continue;
            }

            await context.Migrations.Where(m => m.Email == email)
                .ExecuteUpdateAsync(m=> m.SetProperty(p => p.Status, MigrationStatus.InWork)
                    .SetProperty(p => p.StartDate, DateTime.Now));

            var sw = new Stopwatch();
            try
            {
                RegionSettings.SetCurrent(configuration["fromRegion"]);
                logger.Debug($"user - {email} start migration");

                sw.Start();
                var migrationCreator = serviceProvider.GetService<MigrationCreator>();
                (var fileName, var newAlias, var totalSize) = await migrationCreator.CreateAsync(configuration["fromAlias"], email, configuration["toRegion"], "");

                logger.Debug($"end creator and start runner");

                using var dbContextTenant = creatorDbContext.CreateDbContext<TenantDbContext>(configuration["toRegion"]);
                dbContextTenant.Tenants.Where(t=> t.Alias == newAlias && t.Status == TenantStatus.Suspended).ExecuteDelete();

                var migrationRunner = serviceProvider.GetService<MigrationRunner>();
                (var alias, var tenantId) = await migrationRunner.RunAsync(fileName, configuration["toRegion"], configuration["fromAlias"], "", totalSize);
                sw.Stop();
                Directory.GetFiles(AppContext.BaseDirectory).Where(f => f.Equals(fileName)).ToList().ForEach(File.Delete);

                if (Directory.Exists(AppContext.BaseDirectory + "\\temp"))
                {
                    Directory.Delete(AppContext.BaseDirectory + "\\temp");
                }

                RegionSettings.SetCurrent(configuration["toRegion"]);
                var tenantManager = serviceProvider.GetService<TenantManager>();
                var userManager = serviceProvider.GetService<UserManager>();
                var studioNotifyService = serviceProvider.GetService<StudioNotifyService>();

                await tenantManager.SetCurrentTenantAsync(tenantId);
                var u = await userManager.GetUserByEmailAsync(email);
                await studioNotifyService.MigrationPersonalToDocspaceAsync(u);
                logger.Debug($"user - {email} migrated to {alias}");
                await context.Migrations.Where(m => m.Email == email)
                    .ExecuteUpdateAsync(m => m.SetProperty(p => p.Status, MigrationStatus.Success)
                        .SetProperty(p => p.EndDate, DateTime.Now)
                        .SetProperty(p => p.Alias, alias)
                        .SetProperty(p => p.MigtationTime, sw.Elapsed));
            }
            catch (Exception e)
            {
                await context.Migrations.Where(m => m.Email == email)
                    .ExecuteUpdateAsync(m => m.SetProperty(p => p.Status, MigrationStatus.Error)
                        .SetProperty(p => p.MigtationTime, sw.Elapsed)
                        .SetProperty(p => p.EndDate, DateTime.Now));
                logger.ErrorWithException($"user - {email} error", e);
            }
            finally
            {
                RegionSettings.SetCurrent("");
            }
        }
    }
}
