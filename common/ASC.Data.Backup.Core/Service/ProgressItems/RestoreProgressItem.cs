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

namespace ASC.Data.Backup.Services;

[Transient]
public class RestoreProgressItem : BaseBackupProgressItem
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RestoreProgressItem> _logger;
    private readonly ICache _cache;
    private TenantManager _tenantManager;
    private BackupStorageFactory _backupStorageFactory;
    private readonly NotifyHelper _notifyHelper;
    private BackupRepository _backupRepository;
    private readonly CoreBaseSettings _coreBaseSettings;
    private SocketManager _socketManager;

    private string _region;
    private string _upgradesPath;
    private string _serverBaseUri;

    public RestoreProgressItem()
    {
        
    }
    
    public RestoreProgressItem(
        IConfiguration configuration,
        ILogger<RestoreProgressItem> logger,
        ICache cache,
        IServiceScopeFactory serviceScopeFactory,
        NotifyHelper notifyHelper,
        CoreBaseSettings coreBaseSettings)
        : base(serviceScopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _cache = cache;
        _notifyHelper = notifyHelper;
        _coreBaseSettings = coreBaseSettings;
    }

    public BackupStorageType StorageType { get; set; }
    public string StoragePath { get; set; }
    public bool Notify { get; set; }
    public Dictionary<string, string> StorageParams { get; set; }
    public string TempFolder { get; set; }

    public void Init(StartRestoreRequest request, string tempFolder, string upgradesPath, string region = "current")
    {
        Init();
        BackupProgressItemType = BackupProgressItemType.Restore;
        TenantId = request.TenantId;
        NewTenantId = request.TenantId;
        Notify = request.NotifyAfterCompletion;
        StoragePath = request.FilePathOrId;
        StorageType = request.StorageType;
        StorageParams = request.StorageParams;
        TempFolder = tempFolder;
        _upgradesPath = upgradesPath;
        _region = region;
        _serverBaseUri = request.ServerBaseUri;
        Dump = request.Dump;
    }

    protected override async Task DoJob()
    {
        Tenant tenant = null;
        var tempFile = "";
        var socketTenant = TenantId;
        try
        {
            await using var scope = _serviceScopeProvider.CreateAsyncScope();

            _tenantManager = scope.ServiceProvider.GetService<TenantManager>();
            _backupStorageFactory = scope.ServiceProvider.GetService<BackupStorageFactory>();
            _backupRepository = scope.ServiceProvider.GetService<BackupRepository>();
            _socketManager = scope.ServiceProvider.GetService<SocketManager>();

            tenant = await _tenantManager.GetTenantAsync(TenantId);
            _tenantManager.SetCurrentTenant(tenant);
            await _socketManager.RestoreProgressAsync(socketTenant, Dump, 0);

            _notifyHelper.SetServerBaseUri(_serverBaseUri);

            if (Dump)
            {
                var tenants = await _tenantManager.GetTenantsAsync(true);

                foreach(var t in tenants)
                {
                    await _notifyHelper.SendAboutRestoreStartedAsync(t, Notify);
                    t.SetStatus(TenantStatus.Restoring);
                    await _tenantManager.SaveTenantAsync(t);
                }
            }
            else
            {
                await _notifyHelper.SendAboutRestoreStartedAsync(tenant, Notify);
                tenant.SetStatus(TenantStatus.Restoring);
                await _tenantManager.SaveTenantAsync(tenant);
            }

            var restoreTask = scope.ServiceProvider.GetService<RestorePortalTask>();

            var storage = await _backupStorageFactory.GetBackupStorageAsync(StorageType, TenantId, StorageParams);

            tempFile = await storage.DownloadAsync(StoragePath, TempFolder);

            if (!_coreBaseSettings.Standalone)
            {
                var shaHash = BackupWorker.GetBackupHashSHA(tempFile);
                var record = await _backupRepository.GetBackupRecordAsync(shaHash, TenantId);

                if (record == null)
                {
                    var md5Hash = await BackupWorker.GetBackupHashMD5Async(tempFile, S3Storage.ChunkSize);
                    record = await _backupRepository.GetBackupRecordAsync(md5Hash, TenantId);
                    if (record == null)
                    {
                        throw new Exception(BackupResource.BackupNotFound);
                    }
                }
            }

            Percentage = 10;

            var columnMapper = new ColumnMapper();
            columnMapper.SetMapping("tenants_tenants", "alias", tenant.Alias, Guid.Parse(Id).ToString("N"));
            columnMapper.Commit();

            restoreTask.Init(_region, tempFile, Dump, TenantId, columnMapper, _upgradesPath);
            restoreTask.ProgressChanged = async args =>
            {
                Percentage = Percentage = 10d + 0.65 * args.Progress;
                await _socketManager.RestoreProgressAsync(socketTenant, Dump, (int)Percentage);
                await PublishChanges();
            };
            await restoreTask.RunJob(); 
            NewTenantId = columnMapper.GetTenantMapping();

            await _socketManager.RestoreProgressAsync(socketTenant, Dump, (int)Percentage);
            await PublishChanges();

            if (restoreTask.Dump)
            {
                _cache.Reset();

                var tenants = await _tenantManager.GetTenantsAsync();
                foreach (var t in tenants)
                {
                    await _notifyHelper.SendAboutRestoreCompletedAsync(t, Notify);
                }
            }
            else
            {
                var restoredTenant = await _tenantManager.GetTenantAsync(columnMapper.GetTenantMapping());
                restoredTenant.SetStatus(TenantStatus.Active);
                restoredTenant.Alias = tenant.Alias;
                restoredTenant.PaymentId = string.IsNullOrEmpty(restoredTenant.PaymentId) ? _configuration["core:payment:region"] + TenantId : restoredTenant.PaymentId;

                if (string.IsNullOrEmpty(restoredTenant.MappedDomain) && !string.IsNullOrEmpty(tenant.MappedDomain))
                {
                    restoredTenant.MappedDomain = tenant.MappedDomain;
                }

                await _tenantManager.RestoreTenantAsync(tenant, restoredTenant);
                TenantId = restoredTenant.Id;

                try
                {
                    await _notifyHelper.SendAboutRestoreCompletedAsync(restoredTenant, Notify);
                }
                catch(Exception error)
                {
                    _logger.ErrorNotifyComplete(error);
                }
            }

            Percentage = 75;
            try
            {
                await _socketManager.RestoreProgressAsync(socketTenant, Dump, (int)Percentage);
                await PublishChanges();

                File.Delete(tempFile);
            }
            catch(Exception error)
            {
                _logger.ErrorDeleteFiles(error);
            }

            Percentage = 100;
            IsCompleted = true;
        }
        catch (DbBackupException error)
        {
            _logger.ErrorRestoreProgressItem(error);
            Exception = new DbBackupException(BackupResource.BackupInvalid, error);
            IsCompleted = true;

            if (tenant != null)
            {
                tenant.SetStatus(TenantStatus.Active);
                await _tenantManager.SaveTenantAsync(tenant);
            }
        }
        catch (Exception error)
        {
            _logger.ErrorRestoreProgressItem(error);
            Exception = error;
            IsCompleted = true;

            if (tenant != null)
            {
                if (Dump)
                {
                    var tenants = await _tenantManager.GetTenantsAsync(false);
                    foreach (var t in tenants.Where(t => t.Status == TenantStatus.Restoring))
                    {
                        t.SetStatus(TenantStatus.Active);
                        await _tenantManager.SaveTenantAsync(t);
                    }
                }
                else
                {
                    tenant.SetStatus(TenantStatus.Active);
                    await _tenantManager.SaveTenantAsync(tenant);
                }
            }
        }
        finally
        {
            try
            {
                await _socketManager.EndRestoreAsync(socketTenant, Dump, ToBackupProgress());
                await PublishChanges();
            }
            catch (Exception error)
            {
                _logger.ErrorPublish(error);
            }

            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public override object Clone()
    {
        return MemberwiseClone();
    }
}
