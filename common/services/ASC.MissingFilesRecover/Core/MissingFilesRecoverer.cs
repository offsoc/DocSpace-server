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

namespace ASC.MigrationFromPersonal;

[Singleton]
public class MissingFilesRecoverer(IDbContextFactory<MigrationContext> dbContextFactory,
    CreatorDbContext creatorDbContext,
    IConfiguration configuration,
    ILogger<MissingFilesRecoverer> logger,
    StorageFactory storageFactory,
    ModuleProvider moduleProvider)
{
    public async Task ExecuteAsync()
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var migrations = context.Migrations.Where(q => q.Status == MigrationStatus.Success).ToList();
        var moduleName = "files";
        foreach (var migration in migrations)
        {
            try
            {
                RegionSettings.SetCurrent(configuration["toRegion"]);
                using var dbContextFiles = creatorDbContext.CreateDbContext<FilesDbContext>(configuration["toRegion"]);
                logger.LogDebug($"start alias - {migration.Alias}");

                var tenant = await dbContextFiles.Tenants.SingleOrDefaultAsync(q => q.Alias == migration.Alias);
                if (tenant == null)
                {
                    logger.Warning($"tenant not found - {migration.Alias}");
                    continue;
                }
                var files = dbContextFiles.Files.Where(q => q.CurrentVersion && q.TenantId == tenant.Id).ToList();

                var storage = await storageFactory.GetStorageAsync(tenant.Id, moduleName, configuration["toRegion"]);
                storage.SetQuotaController(null);

                var notFoundFiles = new List<DbFile>();
                var tasks = new List<Task>(20);
                foreach (var file in files)
                {
                    if (tasks.Count == 20)
                    {
                        Task.WaitAll(tasks.ToArray());
                        tasks.Clear();
                    }
                    tasks.Add(FindFiles(notFoundFiles, storage, file));
                }
                Task.WaitAll(tasks.ToArray());

                RegionSettings.SetCurrent(configuration["fromRegion"]);
                if (notFoundFiles.Any())
                {
                    using var dbContextUser = creatorDbContext.CreateDbContext<UserDbContext>(configuration["fromRegion"]);
                    var fromTenant = await dbContextUser.Tenants.Where(q => q.Alias == configuration["fromAlias"]).SingleOrDefaultAsync();
                    if (fromTenant == null)
                    {
                        logger.Warning($"fromTenant not found - {configuration["fromAlias"]}");
                        continue;
                    }
                    var user = await dbContextUser.Users.SingleOrDefaultAsync(q => q.Email == migration.Email && q.TenantId == fromTenant.Id && !q.Removed);
                    if (user == null)
                    {
                        logger.Warning($"user not found - email:{migration.Email}, tenant:{fromTenant.Id}");
                        continue;
                    }
                    var fromStorage = await storageFactory.GetStorageAsync(fromTenant.Id, moduleName, configuration["fromRegion"]);
                    using var dbContextFilesFromRegion = creatorDbContext.CreateDbContext<FilesDbContext>(configuration["fromRegion"]);
                    foreach (var file in notFoundFiles)
                    {
                        logger.Debug($"try find file - title:{file.Title} createBy:{user.Id} tenant:{fromTenant.Id} contentLength:{file.ContentLength} version:{file.Version} comment: {file.Comment} create_on: {file.CreateOn}");
                        var f = await dbContextFilesFromRegion.Files.FirstOrDefaultAsync(q => q.Title == file.Title && q.CreateBy == user.Id && q.TenantId == fromTenant.Id && q.ContentLength == file.ContentLength && q.Version == file.Version && q.Comment == file.Comment && q.CreateOn == file.CreateOn);
                        if (f == null)
                        {
                            logger.Warning($"file like {file.Id} not found");
                            continue;
                        }
                        var filePaths = await fromStorage.ListFilesRelativeAsync(string.Empty, $"\\{GetUniqFileDirectory(f.Id)}", "*.*", true).Where(q=> !q.Contains("thumb")).ToListAsync();

                        if (!filePaths.Any())
                        {
                            logger.Warning($"{f.Id} - source file path was not found");
                        }
                        foreach (var path in filePaths)
                        {
                            var module = moduleProvider.GetByStorageModule(moduleName, string.Empty);
                            var from = $"\\{GetUniqFileDirectory(f.Id)}/{path}";
                            var to = $"\\{GetUniqFileDirectory(file.Id)}/{path}";
                            await using var stream = await fromStorage.GetReadStreamAsync(string.Empty, from);
                            await storage.SaveAsync(string.Empty, to, stream);
                            logger.LogDebug($"file copied from - {from} to - {to}");
                        }
                    }
                }
                else
                {
                    logger.LogDebug($"all files found");
                }
                logger.LogDebug($"end alias - {migration.Alias}");
            }
            catch(Exception e)
            {
                logger.ErrorWithException("something went wrong", e);
                throw;
            }
        }
    }

    private async Task FindFiles(List<DbFile> list, IDataStore store, DbFile dbFile)
    {
        var any = await store.ListFilesRelativeAsync(string.Empty, $"\\{GetUniqFileDirectory(dbFile.Id)}", "*.*", true).AnyAsync();

        if (!any)
        {
            list.Add(dbFile);
            logger.LogDebug($"file {dbFile.Id} not found");
        }
    }

    private string GetUniqFileDirectory(int fileId)
    {
        if (fileId == 0)
        {
            throw new ArgumentNullException("fileIdObject");
        }

        return string.Format("folder_{0}/file_{1}", (fileId / 1000 + 1) * 1000, fileId);
    }
}
