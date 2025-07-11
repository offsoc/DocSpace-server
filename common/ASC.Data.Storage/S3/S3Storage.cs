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

namespace ASC.Data.Storage.S3;

[Scope]
public class S3Storage(TempStream tempStream,
        TenantManager tenantManager,
        PathUtils pathUtils,
        EmailValidationKeyProvider emailValidationKeyProvider,
        IHttpContextAccessor httpContextAccessor,
        ILoggerProvider factory,
        ILogger<S3Storage> options,
        IHttpClientFactory clientFactory,
        TenantQuotaFeatureStatHelper tenantQuotaFeatureStatHelper,
        QuotaSocketManager quotaSocketManager,
        CoreBaseSettings coreBaseSettings,
        IFusionCache cache,
        SettingsManager settingsManager,
        IQuotaService quotaService,
        UserManager userManager,
        CustomQuota customQuota)
    : BaseStorage(tempStream, tenantManager, pathUtils, emailValidationKeyProvider, httpContextAccessor, factory, options, clientFactory, tenantQuotaFeatureStatHelper, quotaSocketManager, settingsManager, quotaService, userManager, customQuota)
{
    public override bool IsSupportCdnUri => true;
    public static long ChunkSize => 1000 * 1024 * 1024;
    public override bool IsSupportChunking => true;

    private readonly List<string> _domains = [];
    private Dictionary<string, S3CannedACL> _domainsAcl;
    private S3CannedACL _moduleAcl;
    private string _accessKeyId = string.Empty;
    private string _bucket = string.Empty;
    private string _recycleDir = string.Empty;
    private bool _recycleUse;
    private Uri _bucketRoot;
    private Uri _bucketSSlRoot;
    private string _region = "";
    private string _serviceurl;
    private bool _forcepathstyle;
    private string _secretAccessKeyId = string.Empty;
    private readonly ServerSideEncryptionMethod _sse = ServerSideEncryptionMethod.AES256;
    private bool _useHttp = true;
    private bool _lowerCasing = true;
    private bool _cdnEnabled;
    private string _cdnKeyPairId;
    private string _cdnPrivateKeyPath;
    public string CdnDistributionDomain { get; private set; }
    private string _subDir = "";

    private EncryptionMethod _encryptionMethod = EncryptionMethod.None;
    private string _encryptionKey;

    public Uri GetUriInternal(string path)
    {
        return new Uri(SecureHelper.IsSecure(_httpContextAccessor?.HttpContext, _options) ? _bucketSSlRoot : _bucketRoot, path ?? "");
    }

    public Uri GetUriShared(string domain, string path)
    {
        return new Uri(SecureHelper.IsSecure(_httpContextAccessor?.HttpContext, _options) ? _bucketSSlRoot : _bucketRoot, MakePath(domain, path));
    }

    public override Task<Uri> GetInternalUriAsync(string domain, string path, TimeSpan expire, IEnumerable<string> headers)
    {
        if (expire == TimeSpan.Zero || expire == TimeSpan.MinValue || expire == TimeSpan.MaxValue)
        {
            expire = GetExpire(domain);
        }
        if (expire == TimeSpan.Zero || expire == TimeSpan.MinValue || expire == TimeSpan.MaxValue)
        {
            return Task.FromResult(GetUriShared(domain, path));
        }

        var pUrlRequest = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Expires = DateTime.UtcNow.Add(expire),
            Key = MakePath(domain, path),
            Protocol = SecureHelper.IsSecure(_httpContextAccessor?.HttpContext, _options) ? Protocol.HTTPS : Protocol.HTTP,
            Verb = HttpVerb.GET
        };

        if (headers != null && headers.Any())
        {
            var headersOverrides = new ResponseHeaderOverrides();

            foreach (var h in headers)
            {
                if (h.StartsWith(Constants.SecureKeyHeader))
                {
                    continue;
                }
                
                if (h.StartsWith("Content-Disposition"))
                {
                    headersOverrides.ContentDisposition = (h[("Content-Disposition".Length + 1)..]);
                }
                else if (h.StartsWith("Cache-Control"))
                {
                    headersOverrides.CacheControl = (h[("Cache-Control".Length + 1)..]);
                }
                else if (h.StartsWith("Content-Encoding"))
                {
                    headersOverrides.ContentEncoding = (h[("Content-Encoding".Length + 1)..]);
                }
                else if (h.StartsWith("Content-Language"))
                {
                    headersOverrides.ContentLanguage = (h[("Content-Language".Length + 1)..]);
                }
                else if (h.StartsWith("Content-Type"))
                {
                    headersOverrides.ContentType = (h[("Content-Type".Length + 1)..]);
                }
                else if (h.StartsWith("Expires"))
                {
                    headersOverrides.Expires = (h[("Expires".Length + 1)..]);
                }
                else
                {
                    throw new FormatException(string.Format("Invalid header: {0}", h));
                }
            }

            pUrlRequest.ResponseHeaderOverrides = headersOverrides;
        }

        using var client = GetClient();

        return Task.FromResult(MakeUri(client.GetPreSignedURL(pUrlRequest)));
    }

    public override Task<Uri> GetCdnPreSignedUriAsync(string domain, string path, TimeSpan expire, IEnumerable<string> headers)
    {
        if (!_cdnEnabled)
        {
            return GetInternalUriAsync(domain, path, expire, headers);
        }

        var proto = SecureHelper.IsSecure(_httpContextAccessor?.HttpContext, _options) ? "https" : "http";

        var baseUrl = $"{proto}://{CdnDistributionDomain}/{MakePath(domain, path)}";

        var uriBuilder = new UriBuilder(baseUrl)
        {
            Port = -1
        };

        var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);

        if (headers != null && headers.Any())
        {
            foreach (var h in headers)
            {
                if (h.StartsWith("Content-Disposition"))
                {
                    queryParams["response-content-disposition"] = h[("Content-Disposition".Length + 1)..];
                }
                else if (h.StartsWith("Cache-Control"))
                {
                    queryParams["response-cache-control"] = h[("Cache-Control".Length + 1)..];
                }
                else if (h.StartsWith("Content-Encoding"))
                {
                    queryParams["response-content-encoding"] = h[("Content-Encoding".Length + 1)..];
                }
                else if (h.StartsWith("Content-Language"))
                {
                    queryParams["response-content-language"] = h[("Content-Language".Length + 1)..];
                }
                else if (h.StartsWith("Content-Type"))
                {
                    queryParams["response-content-type"] = h[("Content-Type".Length + 1)..];
                }
                else if (h.StartsWith("Expires"))
                {
                    queryParams["response-expires"] = h[("Expires".Length + 1)..];
                }
                else if (h.StartsWith("Custom-Cache-Key"))
                {
                    queryParams["custom-cache-key"] = h[("Custom-Cache-Key".Length + 1)..];
                }
                else
                {
                    throw new FormatException(string.Format("Invalid header: {0}", h));
                }
            }
        }

        uriBuilder.Query = queryParams.ToString();

        string signedUrl;

        using (TextReader textReader = File.OpenText(_cdnPrivateKeyPath))
        {
            signedUrl = AmazonCloudFrontUrlSigner.GetCannedSignedURL(
                      uriBuilder.ToString(),
                      textReader,
                      _cdnKeyPairId,
                      DateTime.UtcNow.Add(expire));
        }


        var signedUri = new Uri(signedUrl, new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });

        return Task.FromResult(signedUri);
    }

    public override Task<Stream> GetReadStreamAsync(string domain, string path)
    {
        return GetReadStreamAsync(domain, path, 0);
    }

    public override async Task<Stream> GetReadStreamAsync(string domain, string path, long offset)
    {
        return await GetReadStreamAsync(domain, path, offset, long.MaxValue);
    }

    public override async Task<Stream> GetReadStreamAsync(string domain, string path, long offset, long length)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucket,
            Key = MakePath(domain, path)
        };

        if (length> 0 && (offset > 0 || offset == 0 && length != long.MaxValue))
        {
            request.ByteRange = new ByteRange(offset, length == int.MaxValue ? length : offset + length - 1);
        }

        try
        {
            using var client = GetClient();
            return new ResponseStreamWrapper(await client.GetObjectAsync(request));
        }
        catch (AmazonS3Exception ex)
        {
            if (ex.ErrorCode == "NoSuchKey")
            {
                throw new FileNotFoundException("File not found", request.Key);
            }

            throw;
        }
    }
    public override Task<Uri> SaveAsync(string domain, string path, Guid ownerId, Stream stream, string contentType, string contentDisposition)
    {
        return SaveAsync(domain, path, ownerId, stream, contentType, contentDisposition, ACL.Auto);
    }
    public override Task<Uri> SaveAsync(string domain, string path, Stream stream, string contentType,
                string contentDisposition)
    {
        return SaveAsync(domain, path, stream, contentType, contentDisposition, ACL.Auto);
    }

    private bool EnableQuotaCheck(string domain)
    {
        return (QuotaController != null) && !domain.EndsWith("_temp");
    }

    public async Task<Uri> SaveAsync(string domain, string path, Stream stream, string contentType,
                         string contentDisposition, ACL acl, string contentEncoding = null, int cacheDays = 5)
    {
        return await SaveAsync(domain, path, Guid.Empty, stream, contentType,
                         contentDisposition, acl, contentEncoding, cacheDays);
    }
    public async Task<Uri> SaveAsync(string domain, string path, Guid ownerId, Stream stream, string contentType,
                         string contentDisposition, ACL acl, string contentEncoding = null, int cacheDays = 5)
    {
        var (buffered, isNew) = await _tempStream.TryGetBufferedAsync(stream);

        try
        {
            if (EnableQuotaCheck(domain))
            {
                await QuotaController.QuotaUsedCheckAsync(buffered.Length, ownerId);
            }

            using var client = GetClient();
            using var uploader = new TransferUtility(client);
            var mime = string.IsNullOrEmpty(contentType)
                ? MimeMapping.GetMimeMapping(Path.GetFileName(path))
                : contentType;

            var request = new TransferUtilityUploadRequest
            {
                BucketName = _bucket,
                Key = MakePath(domain, path),
                ContentType = mime,
                ServerSideEncryptionMethod = _sse,
                InputStream = buffered,
                AutoCloseStream = false
            };

            if (client is not IAmazonS3Encryption)
            {
                request.ServerSideEncryptionMethod = GetServerSideEncryptionMethod(out var kmsKeyId);
                request.ServerSideEncryptionKeyManagementServiceKeyId = kmsKeyId;
            }

            request.CannedACL = acl switch
            {
                ACL.Auto => GetDomainACL(domain),
                ACL.Read or ACL.Private => GetS3Acl(acl),
                _ => request.CannedACL
            };

            if (!string.IsNullOrEmpty(contentDisposition))
            {
                request.Headers.ContentDisposition = contentDisposition;
            }
            else if (mime == "application/octet-stream")
            {
                request.Headers.ContentDisposition = "attachment";
            }

            if (!string.IsNullOrEmpty(contentEncoding))
            {
                request.Headers.ContentEncoding = contentEncoding;
            }

            await uploader.UploadAsync(request);

            //await InvalidateCloudFrontAsync(MakePath(domain, path));

            await QuotaUsedAddAsync(domain, buffered.Length, ownerId);

            return await GetUriAsync(domain, path);
        }
        finally
        {
            if (isNew)
            {
                await buffered.DisposeAsync();
            }
        }
    }

    public override Task<Uri> SaveAsync(string domain, string path, Stream stream, Guid ownerId)
    {
        return SaveAsync(domain, path, ownerId, stream, string.Empty, string.Empty);
    }
    public override Task<Uri> SaveAsync(string domain, string path, Stream stream)
    {
        return SaveAsync(domain, path, stream, string.Empty, string.Empty);
    }

    public override Task<Uri> SaveAsync(string domain, string path, Stream stream, string contentEncoding, int cacheDays)
    {
        return SaveAsync(domain, path, stream, string.Empty, string.Empty, ACL.Auto, contentEncoding, cacheDays);
    }

    public override Task<Uri> SaveAsync(string domain, string path, Stream stream, ACL acl)
    {
        return SaveAsync(domain, path, stream, null, null, acl);
    }

    #region chunking

    public override async Task<string> InitiateChunkedUploadAsync(string domain, string path)
    {
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = MakePath(domain, path)
        };

        using var s3 = GetClient();
        if (s3 is not IAmazonS3Encryption)
        {
            request.ServerSideEncryptionMethod = GetServerSideEncryptionMethod(out var kmsKeyId);
            request.ServerSideEncryptionKeyManagementServiceKeyId = kmsKeyId;
        }
        var response = await s3.InitiateMultipartUploadAsync(request);

        return response.UploadId;
    }

    public override async Task<string> UploadChunkAsync(string domain, string path, string uploadId, Stream stream, long defaultChunkSize, int chunkNumber, long chunkLength)
    {
        Stream bufferStream = null;
        
        var request = new UploadPartRequest
        {
            BucketName = _bucket,
            Key = MakePath(domain, path),
            UploadId = uploadId,
            PartNumber = chunkNumber
        };

        if (stream.CanSeek)
        {
            request.InputStream = stream;
        }
        else
        { 
            bufferStream = _tempStream.Create();
            await stream.CopyToAsync(bufferStream);
            bufferStream.Position = 0;
            
            request.InputStream = bufferStream;
        }

        try
        {
            using var s3 = GetClient();
            var response = await s3.UploadPartAsync(request);

            return response.ETag;
        }
        catch (AmazonS3Exception error)
        {
            if (error.ErrorCode == "NoSuchUpload")
            {
                await AbortChunkedUploadAsync(domain, path, uploadId);
            }

            throw;
        }
        finally
        {
            if (bufferStream != null)
            {
                await bufferStream.DisposeAsync();
            }
        }
    }

    public override async Task<Uri> FinalizeChunkedUploadAsync(string domain, string path, string uploadId, Dictionary<int, string> eTags)
    {
        var request = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = MakePath(domain, path),
            UploadId = uploadId,
            PartETags = eTags.Select(x => new PartETag(x.Key, x.Value)).ToList()
        };

        try
        {
            using (var s3 = GetClient())
            {
                await s3.CompleteMultipartUploadAsync(request);
                //    await InvalidateCloudFrontAsync(MakePath(domain, path));
            }

            if (QuotaController != null)
            {
                var size = await GetFileSizeAsync(domain, path);
                await QuotaUsedAddAsync(domain, size);
            }

            return await GetUriAsync(domain, path);
        }
        catch (AmazonS3Exception error)
        {
            if (error.ErrorCode == "NoSuchUpload")
            {
                await AbortChunkedUploadAsync(domain, path, uploadId);
            }

            throw;
        }
    }

    public override async Task AbortChunkedUploadAsync(string domain, string path, string uploadId)
    {
        var key = MakePath(domain, path);

        var request = new AbortMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = key,
            UploadId = uploadId
        };

        using var s3 = GetClient();
        await s3.AbortMultipartUploadAsync(request);
    }

    public override IDataWriteOperator CreateDataWriteOperator(CommonChunkedUploadSession chunkedUploadSession,
            CommonChunkedUploadSessionHolder sessionHolder, bool isConsumerStorage = false)
    {
        if (coreBaseSettings.Standalone || isConsumerStorage)
        {
            return new S3ZipWriteOperator(_tempStream, chunkedUploadSession, sessionHolder);
        }

        return new S3TarWriteOperator(chunkedUploadSession, sessionHolder, _tempStream, cache);
    }

    public override string GetBackupExtension(bool isConsumerStorage = false)
    {
        if (coreBaseSettings.Standalone || isConsumerStorage)
        {
            return "tar.gz";
        }

        return "tar";
    }

    #endregion

    public override async Task DeleteAsync(string domain, string path)
    {
        using var client = GetClient();
        var key = MakePath(domain, path);
        var size = await GetFileSizeAsync(domain, path);

        await RecycleAsync(client, domain, key);

        var request = new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = key
        };

        await client.DeleteObjectAsync(request);

        await QuotaUsedDeleteAsync(domain, size);
    }

    public override async Task DeleteFilesAsync(string domain, List<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var keysToDel = new List<string>();

        long quotaUsed = 0;

        foreach (var path in paths)
        {
            try
            {
                //var obj = GetS3Objects(domain, path).FirstOrDefault();

                var key = MakePath(domain, path);

                if (QuotaController != null)
                {
                    quotaUsed += await GetFileSizeAsync(domain, path);
                }

                keysToDel.Add(key);

                //objsToDel.Add(obj);
            }
            catch (FileNotFoundException)
            {

            }
        }

        if (keysToDel.Count == 0)
        {
            return;
        }

        using (var client = GetClient())
        {
            var deleteRequest = new DeleteObjectsRequest
            {
                BucketName = _bucket,
                Objects = keysToDel.Select(key => new KeyVersion { Key = key }).ToList()
            };

            await client.DeleteObjectsAsync(deleteRequest);
        }

        if (quotaUsed > 0)
        {
            await QuotaUsedDeleteAsync(domain, quotaUsed);
        }
    }

    public override async Task DeleteFilesAsync(string domain, string path, string pattern, bool recursive)
    {
        await DeleteFilesAsync(domain, path, pattern, recursive, Guid.Empty);
    }
    public override async Task DeleteFilesAsync(string domain, string path, string pattern, bool recursive, Guid ownerId)
    {
        var makedPath = MakePath(domain, path) + '/';
        var obj = await GetS3ObjectsAsync(domain, path);
        var objToDel = obj.Where(x =>
            Wildcard.IsMatch(pattern, Path.GetFileName(x.Key))
            && (recursive || !x.Key.Remove(0, makedPath.Length).Contains('/'))
            );

        using var client = GetClient();
        foreach (var s3Object in objToDel)
        {
            await RecycleAsync(client, domain, s3Object.Key);

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = s3Object.Key
            };

            await client.DeleteObjectAsync(deleteRequest);

            if (QuotaController != null)
            {
                if (string.IsNullOrEmpty(QuotaController.ExcludePattern) ||
                    !Path.GetFileName(s3Object.Key).StartsWith(QuotaController.ExcludePattern))
                {
                    await QuotaUsedDeleteAsync(domain, s3Object.Size, ownerId);
                }
            }
        }
    }

    public override async Task DeleteFilesAsync(string domain, string path, DateTime fromDate, DateTime toDate)
    {
        var obj = await GetS3ObjectsAsync(domain, path);
        var objToDel = obj.Where(x => x.LastModified >= fromDate && x.LastModified <= toDate);

        using var client = GetClient();
        foreach (var s3Object in objToDel)
        {
            await RecycleAsync(client, domain, s3Object.Key);

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = s3Object.Key
            };

            await client.DeleteObjectAsync(deleteRequest);

            await QuotaUsedDeleteAsync(domain, s3Object.Size);
        }
    }

    public override async Task MoveDirectoryAsync(string srcDomain, string srcDir, string newDomain, string newDir)
    {
        var srckey = MakePath(srcDomain, srcDir);
        var dstkey = MakePath(newDomain, newDir);
        //List files from src
        using var client = GetClient();
        var request = new ListObjectsRequest
        {
            BucketName = _bucket,
            Prefix = srckey
        };

        var response = await client.ListObjectsAsync(request);
        foreach (var s3Object in response.S3Objects)
        {
            await CopyFileAsync(client, s3Object.Key, s3Object.Key.Replace(srckey, dstkey), newDomain);

            await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = s3Object.Key
            });
        }
    }

    public override async Task<Uri> MoveAsync(string srcDomain, string srcPath, string newDomain, string newPath, bool quotaCheckFileSize = true)
    {
        return await MoveAsync(srcDomain, srcPath, newDomain, newPath, Guid.Empty, quotaCheckFileSize);
    }
    public override async Task<Uri> MoveAsync(string srcDomain, string srcPath, string newDomain, string newPath, Guid ownerId, bool quotaCheckFileSize = true)
    {
        var srcKey = MakePath(srcDomain, srcPath);
        var dstKey = MakePath(newDomain, newPath);
        var size = await GetFileSizeAsync(srcDomain, srcPath);

        using var client = GetClient();
        await CopyFileAsync(client, srcKey, dstKey, newDomain, S3MetadataDirective.REPLACE);
        await DeleteAsync(srcDomain, srcPath);

        await QuotaUsedDeleteAsync(srcDomain, size);
        await QuotaUsedAddAsync(newDomain, size, ownerId, quotaCheckFileSize);

        return await GetUriAsync(newDomain, newPath);
    }

    public override async Task<(Uri, string)> SaveTempAsync(string domain, Stream stream)
    {
        var assignedPath = Guid.NewGuid().ToString();
        return (await SaveAsync(domain, assignedPath, stream), assignedPath);
    }

    public override async IAsyncEnumerable<string> ListDirectoriesRelativeAsync(string domain, string path, bool recursive)
    {
        var tmp = await GetS3ObjectsAsync(domain, path);
        var obj = tmp
            .Where(x => x.Key.EndsWith('/'))
            .Select(x => x.Key[(MakePath(domain, path) + "/").Length..]);
        foreach (var e in obj)
        {
            yield return e;
        }
    }

    public override async Task<string> SavePrivateAsync(string domain, string path, Stream stream, DateTime expires)
    {
        using var client = GetClient();
        using var uploader = new TransferUtility(client);
        var objectKey = MakePath(domain, path);
        var (buffered, isNew) = await _tempStream.TryGetBufferedAsync(stream);
        try
        {
            var request = new TransferUtilityUploadRequest
            {
                BucketName = _bucket,
                Key = objectKey,
                CannedACL = S3CannedACL.BucketOwnerFullControl,
                ContentType = "application/octet-stream",
                InputStream = buffered,
                Headers =
                    {
                        CacheControl = $"public, maxage={(int)TimeSpan.FromDays(5).TotalSeconds}",
                        ExpiresUtc = DateTime.UtcNow.Add(TimeSpan.FromDays(5)),
                        ContentDisposition = "attachment"
                    }
            };

            request.Metadata.Add("private-expire", expires.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture));

            await uploader.UploadAsync(request);
        }
        finally
        {
            if (isNew)
            {
                await buffered.DisposeAsync();
            }
        }

        //Get presigned url                
        var pUrlRequest = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Expires = expires,
            Key = objectKey,
            Protocol = Protocol.HTTP,
            Verb = HttpVerb.GET
        };

        var url = await client.GetPreSignedURLAsync(pUrlRequest);
        //TODO: CNAME!
        return url;
    }

    public override async Task DeleteExpiredAsync(string domain, string path, TimeSpan oldThreshold)
    {
        using var client = GetClient();
        var s3Obj = await GetS3ObjectsAsync(domain, path);
        foreach (var s3Object in s3Obj)
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucket,
                Key = s3Object.Key
            };

            var metadata = await client.GetObjectMetadataAsync(request);
            var privateExpireKey = metadata.Metadata["private-expire"];
            if (string.IsNullOrEmpty(privateExpireKey))
            {
                continue;
            }

            if (!long.TryParse(privateExpireKey, out var fileTime))
            {
                continue;
            }

            if (DateTime.UtcNow <= DateTime.FromFileTimeUtc(fileTime))
            {
                continue;
            }
            //Delete it
            var deleteObjectRequest = new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = s3Object.Key
            };

            await client.DeleteObjectAsync(deleteObjectRequest);
        }
    }

    public override string GetUploadUrl()
    {
        return GetUriInternal(string.Empty).ToString();
    }

    public override string GetPostParams(string domain, string directoryPath, long maxUploadSize, string contentType,
                                         string contentDisposition)
    {
        var key = MakePath(domain, directoryPath) + "/";
        //Generate policy
        var policyBase64 = GetPolicyBase64(key, string.Empty, contentType, contentDisposition, maxUploadSize,
                                              out var sign);
        var postBuilder = new StringBuilder();
        postBuilder.Append('{');
        postBuilder.Append("\"key\":\"").Append(key).Append("${{filename}}\",");
        postBuilder.Append("\"acl\":\"public-read\",");
        postBuilder.Append($"\"key\":\"{key}\",");
        postBuilder.Append("\"success_action_status\":\"201\",");

        if (!string.IsNullOrEmpty(contentType))
        {
            postBuilder.Append($"\"Content-Type\":\"{contentType}\",");
        }

        if (!string.IsNullOrEmpty(contentDisposition))
        {
            postBuilder.Append($"\"Content-Disposition\":\"{contentDisposition}\",");
        }

        postBuilder.Append($"\"AWSAccessKeyId\":\"{_accessKeyId}\",");
        postBuilder.Append($"\"Policy\":\"{policyBase64}\",");
        postBuilder.Append($"\"Signature\":\"{sign}\"");
        postBuilder.Append("\"SignatureVersion\":\"2\"");
        postBuilder.Append("\"SignatureMethod\":\"HmacSHA1\"");
        postBuilder.Append('}');

        return postBuilder.ToString();
    }

    public override string GetUploadForm(string domain, string directoryPath, string redirectTo, long maxUploadSize,
                                         string contentType, string contentDisposition, string submitLabel)
    {
        var destBucket = GetUploadUrl();
        var key = MakePath(domain, directoryPath) + "/";
        //Generate policy
        var policyBase64 = GetPolicyBase64(key, redirectTo, contentType, contentDisposition, maxUploadSize,
                                              out var sign);

        var formBuilder = new StringBuilder();
        formBuilder.Append($"<form action=\"{destBucket}\" method=\"post\" enctype=\"multipart/form-data\">");
        formBuilder.Append($"<input type=\"hidden\" name=\"key\" value=\"{key}${{filename}}\" />");
        formBuilder.Append("<input type=\"hidden\" name=\"acl\" value=\"public-read\" />");
        if (!string.IsNullOrEmpty(redirectTo))
        {
            formBuilder.Append($"<input type=\"hidden\" name=\"success_action_redirect\" value=\"{redirectTo}\" />");
        }

        formBuilder.Append($"<input type=\"hidden\" name=\"success_action_status\" value=\"{201}\" />");

        if (!string.IsNullOrEmpty(contentType))
        {
            formBuilder.Append($"<input type=\"hidden\" name=\"Content-Type\" value=\"{contentType}\" />");
        }

        if (!string.IsNullOrEmpty(contentDisposition))
        {
            formBuilder.Append($"<input type=\"hidden\" name=\"Content-Disposition\" value=\"{contentDisposition}\" />");
        }

        formBuilder.Append($"<input type=\"hidden\" name=\"AWSAccessKeyId\" value=\"{_accessKeyId}\"/>");
        formBuilder.Append($"<input type=\"hidden\" name=\"Policy\" value=\"{policyBase64}\" />");
        formBuilder.Append($"<input type=\"hidden\" name=\"Signature\" value=\"{sign}\" />");
        formBuilder.Append("<input type=\"hidden\" name=\"SignatureVersion\" value=\"2\" />");
        formBuilder.Append("<input type=\"hidden\" name=\"SignatureMethod\" value=\"HmacSHA1{0}\" />");
        formBuilder.Append("<input type=\"file\" name=\"file\" />");
        formBuilder.Append($"<input type=\"submit\" name=\"submit\" value=\"{submitLabel}\" /></form>");

        return formBuilder.ToString();
    }

    public override async IAsyncEnumerable<string> ListFilesRelativeAsync(string domain, string path, string pattern, bool recursive)
    {
        var tmp = await GetS3ObjectsAsync(domain, path);
        var obj = tmp.Where(x=> !x.Key.EndsWith('/'))
            .Where(x => Wildcard.IsMatch(pattern, Path.GetFileName(x.Key)))
            .Select(x => x.Key[(MakePath(domain, path) + "/").Length..].TrimStart('/'));

        foreach (var e in obj)
        {
            yield return e;
        }
    }

    public override async Task<bool> IsFileAsync(string domain, string path)
    {
        using var client = GetClient();
        try
        {
            var getObjectMetadataRequest = new GetObjectMetadataRequest
            {
                BucketName = _bucket,
                Key = MakePath(domain, path)
            };

            await client.GetObjectMetadataAsync(getObjectMetadataRequest);

            return true;
        }
        catch (AmazonS3Exception ex)
        {
            if (string.Equals(ex.ErrorCode, "NoSuchBucket"))
            {
                return false;
            }

            if (string.Equals(ex.ErrorCode, "NotFound"))
            {
                return false;
            }

            throw;
        }
    }

    public override async Task<bool> IsDirectoryAsync(string domain, string path)
    {
        using var client = GetClient();
        var request = new ListObjectsRequest { BucketName = _bucket, Prefix = MakePath(domain, path) };
        var response = await client.ListObjectsAsync(request);

        return response.S3Objects.Count > 0;
    }

    public override async Task DeleteDirectoryAsync(string domain, string path)
    {
        await DeleteDirectoryAsync(domain, path, Guid.Empty);
    }
    public override async Task DeleteDirectoryAsync(string domain, string path, Guid ownerId)
    {
        await DeleteFilesAsync(domain, path, "*", true, ownerId);
    }

    public override async Task<long> GetFileSizeAsync(string domain, string path)
    {
        using var client = GetClient();
        var request = new ListObjectsRequest { BucketName = _bucket, Prefix = MakePath(domain, path) };
        var response = await client.ListObjectsAsync(request);
        if (response.S3Objects.Count > 0)
        {
            return response.S3Objects[0].Size;
        }

        throw new FileNotFoundException("file not found", path);
    }

    public override async Task<long> GetDirectorySizeAsync(string domain, string path)
    {
        if (!await IsDirectoryAsync(domain, path))
        {
            throw new FileNotFoundException("directory not found", path);
        }

        var tmp = await GetS3ObjectsAsync(domain, path);
        return tmp.Where(x => Wildcard.IsMatch("*.*", Path.GetFileName(x.Key)))
        .Sum(x => x.Size);
    }

    public override async Task<long> ResetQuotaAsync(string domain)
    {
        if (QuotaController != null)
        {
            var objects = await GetS3ObjectsAsync(domain);
            var size = objects.Sum(s3Object => s3Object.Size);
            await QuotaController.QuotaUsedSetAsync(Modulename, domain, DataList.GetData(domain), size);

            return size;
        }

        return 0;
    }

    public override async Task<long> GetUsedQuotaAsync(string domain)
    {
        var objects = await GetS3ObjectsAsync(domain);

        return objects.Sum(s3Object => s3Object.Size);
    }

    public override async Task<Uri> CopyAsync(string srcDomain, string srcpath, string newDomain, string newPath)
    {
        var srcKey = MakePath(srcDomain, srcpath);
        var dstKey = MakePath(newDomain, newPath);
        var size = await GetFileSizeAsync(srcDomain, srcpath);
        using var client = GetClient();
        await CopyFileAsync(client, srcKey, dstKey, newDomain, S3MetadataDirective.REPLACE);

        await QuotaUsedAddAsync(newDomain, size);

        return await GetUriAsync(newDomain, newPath);
    }

    public override async Task CopyDirectoryAsync(string srcDomain, string srcdir, string newDomain, string newDir)
    {
        var srckey = MakePath(srcDomain, srcdir);
        var dstkey = MakePath(newDomain, newDir);
        //List files from src
        using var client = GetClient();
        var request = new ListObjectsRequest { BucketName = _bucket, Prefix = srckey };

        var response = await client.ListObjectsAsync(request);
        foreach (var s3Object in response.S3Objects)
        {
            await CopyFileAsync(client, s3Object.Key, s3Object.Key.Replace(srckey, dstkey), newDomain);

            await QuotaUsedAddAsync(newDomain, s3Object.Size);
        }
    }

    public override Task<IDataStore> ConfigureAsync(string tenant, Handler handlerConfig, Module moduleConfig, IDictionary<string, string> props, IDataStoreValidator dataStoreValidator)
    {
        Tenant = tenant;

        if (moduleConfig != null)
        {
            Modulename = moduleConfig.Name;
            DataList = new DataList(moduleConfig);

            _domains.AddRange(moduleConfig.Domain.Select(x => $"{x.Name}/"));

            //Make expires
            DomainsExpires = moduleConfig.Domain.Where(x => x.Expires != TimeSpan.Zero).ToDictionary(x => x.Name, y => y.Expires);
            DomainsExpires.Add(string.Empty, moduleConfig.Expires);

            DomainsContentAsAttachment = moduleConfig.Domain.Where(x => x.ContentAsAttachment.HasValue).ToDictionary(x => x.Name, y => y.ContentAsAttachment.Value);
            DomainsContentAsAttachment.Add(string.Empty, moduleConfig.ContentAsAttachment ?? false);

            _domainsAcl = moduleConfig.Domain.ToDictionary(x => x.Name, y => GetS3Acl(y.Acl));
            _moduleAcl = GetS3Acl(moduleConfig.Acl);
        }
        else
        {
            Modulename = string.Empty;
            DataList = null;

            //Make expires
            DomainsExpires = new Dictionary<string, TimeSpan> { { string.Empty, TimeSpan.Zero } };
            DomainsContentAsAttachment = new Dictionary<string, bool> { { string.Empty, false } };

            _domainsAcl = new Dictionary<string, S3CannedACL>();
            _moduleAcl = S3CannedACL.PublicRead;
        }

        _accessKeyId = props["acesskey"];
        _secretAccessKeyId = props["secretaccesskey"];
        _bucket = props["bucket"];

        props.TryGetValue("recycleDir", out _recycleDir);

        if (props.TryGetValue("recycleUse", out var recycleUseProp) && bool.TryParse(recycleUseProp, out var recycleUse))
        {
            _recycleUse = recycleUse;
        }

        if (props.TryGetValue("region", out var region) && !string.IsNullOrEmpty(region))
        {
            _region = region;
        }

        if (props.TryGetValue("serviceurl", out var url) && !string.IsNullOrEmpty(url))
        {
            _serviceurl = url;
        }

        if (props.TryGetValue("forcepathstyle", out var style) && bool.TryParse(style, out var fps))
        {
            _forcepathstyle = fps;
        }

        if (props.TryGetValue("usehttp", out var use) && bool.TryParse(use, out var uh))
        {
            _useHttp = uh;
        }

        if (props.TryGetValue("sse", out var sse) && !string.IsNullOrEmpty(sse))
        {
            _encryptionMethod = sse.ToLower() switch
            {
                "none" => EncryptionMethod.None,
                "aes256" => EncryptionMethod.ServerS3,
                "awskms" => EncryptionMethod.ServerKms,
                "clientawskms" => EncryptionMethod.ClientKms,
                _ => EncryptionMethod.None
            };
        }

        if (props.ContainsKey("ssekey") && !string.IsNullOrEmpty(props["ssekey"]))
        {
            _encryptionKey = props["ssekey"];
        }

        _bucketRoot = props.ContainsKey("cname") && Uri.IsWellFormedUriString(props["cname"], UriKind.Absolute)
                          ? new Uri(props["cname"], UriKind.Absolute)
                              : new Uri($"http://s3.{_region}.amazonaws.com/{_bucket}/", UriKind.Absolute);
        _bucketSSlRoot = props.ContainsKey("cnamessl") &&
                         Uri.IsWellFormedUriString(props["cnamessl"], UriKind.Absolute)
                             ? new Uri(props["cnamessl"], UriKind.Absolute)
                                 : new Uri($"https://s3.{_region}.amazonaws.com/{_bucket}/", UriKind.Absolute);

        if (props.TryGetValue("lower", out var lower))
        {
            bool.TryParse(lower, out _lowerCasing);
        }

        if (props.TryGetValue("cdn_enabled", out var cdnEnabled))
        {
            if (bool.TryParse(cdnEnabled, out _cdnEnabled))
            {
                _cdnKeyPairId = props["cdn_keyPairId"];
                _cdnPrivateKeyPath = props["cdn_privateKeyPath"];
                CdnDistributionDomain = props["cdn_distributionDomain"];
            }
        }

        props.TryGetValue("subdir", out _subDir);

        DataStoreValidator = dataStoreValidator;
        
        return Task.FromResult<IDataStore>(this);
    }

    protected override Task<Uri> SaveWithAutoAttachmentAsync(string domain, string path, Stream stream, string attachmentFileName)
    {
        return SaveWithAutoAttachmentAsync(domain, path, Guid.Empty, stream, attachmentFileName);
    }
    protected override Task<Uri> SaveWithAutoAttachmentAsync(string domain, string path, Guid ownerId, Stream stream, string attachmentFileName)
    {
        var contentDisposition = $"attachment; filename={HttpUtility.UrlPathEncode(attachmentFileName)};";
        if (attachmentFileName.Any(c => c >= 0 && c <= 127))
        {
            contentDisposition = $"attachment; filename*=utf-8''{HttpUtility.UrlPathEncode(attachmentFileName)};";
        }
        return SaveAsync(domain, path, ownerId, stream, null, contentDisposition);
    }


    private S3CannedACL GetDomainACL(string domain)
    {
        if (GetExpire(domain) != TimeSpan.Zero)
        {
            return S3CannedACL.Private;
        }

        return _domainsAcl.GetValueOrDefault(domain, _moduleAcl);
    }

    private S3CannedACL GetS3Acl(ACL acl)
    {
        return acl switch
        {
            ACL.Read => S3CannedACL.PublicRead,
            ACL.Private => S3CannedACL.Private,
            _ => S3CannedACL.PublicRead
        };
    }

    private Uri MakeUri(string preSignedURL)
    {
        var uri = new UnencodedUri(preSignedURL);
        var signedPart = uri.PathAndQuery.TrimStart('/');

        var baseUri = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? _bucketSSlRoot : _bucketRoot;

        return preSignedURL.StartsWith(baseUri.ToString()) ? uri : new UnencodedUri(baseUri, signedPart);
    }

    private async ValueTask InvalidateCloudFrontAsync(params string[] paths)
    {
        if (!_cdnEnabled || string.IsNullOrEmpty(CdnDistributionDomain))
        {
            return;
        }

        using var cfClient = GetCloudFrontClient();
        var invalidationRequest = new CreateInvalidationRequest
        {
            DistributionId = CdnDistributionDomain,
            InvalidationBatch = new InvalidationBatch
            {
                CallerReference = Guid.NewGuid().ToString(),

                Paths = new Paths
                {
                    Items = paths.ToList(),
                    Quantity = paths.Length
                }
            }
        };

        await cfClient.CreateInvalidationAsync(invalidationRequest);
    }

    private string GetPolicyBase64(string key, string redirectTo, string contentType, string contentDisposition,
                                   long maxUploadSize, out string sign)
    {
        var policyBuilder = new StringBuilder();

        var minutes = DateTime.UtcNow.AddMinutes(15).ToString(AWSSDKUtils.ISO8601DateFormat,
                                                                           CultureInfo.InvariantCulture);

        policyBuilder.Append($"{{\"expiration\": \"{minutes}\",\"conditions\":[");
        policyBuilder.Append($"{{\"bucket\": \"{_bucket}\"}},");
        policyBuilder.Append($"[\"starts-with\", \"$key\", \"{key}\"],");
        policyBuilder.Append("{\"acl\": \"public-read\"},");
        if (!string.IsNullOrEmpty(redirectTo))
        {
            policyBuilder.Append($"{{\"success_action_redirect\": \"{redirectTo}\"}},");
        }
        policyBuilder.Append("{{\"success_action_status\": \"201\"}},");
        if (!string.IsNullOrEmpty(contentType))
        {
            policyBuilder.Append($"[\"eq\", \"$Content-Type\", \"{contentType}\"],");
        }
        if (!string.IsNullOrEmpty(contentDisposition))
        {
            policyBuilder.Append($"[\"eq\", \"$Content-Disposition\", \"{contentDisposition}\"],");
        }
        policyBuilder.Append($"[\"content-length-range\", 0, {maxUploadSize}]");
        policyBuilder.Append("]}");

        var policyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(policyBuilder.ToString()));
        //sign = AWSSDKUtils.HMACSign(policyBase64, _secretAccessKeyId, new HMACSHA1());
        using var algorithm = new HMACSHA1();
        algorithm.Key = Encoding.UTF8.GetBytes(_secretAccessKeyId);
        try
        {
            algorithm.Key = Encoding.UTF8.GetBytes(key);
            sign = Convert.ToBase64String(algorithm.ComputeHash(Encoding.UTF8.GetBytes(policyBase64)));
        }
        finally
        {
            algorithm.Clear();
        }

        return policyBase64;
    }


    private bool CheckKey(string domain, string key)
    {
        return !string.IsNullOrEmpty(domain) ||
               _domains.TrueForAll(configuredDomains => !key.StartsWith(MakePath(configuredDomains, "")));
    }

    private async Task<IEnumerable<S3Object>> GetS3ObjectsByPathAsync(string domain, string path)
    {
        using var client = GetClient();
        var request = new ListObjectsRequest
        {
            BucketName = _bucket,
            Prefix = path,
            MaxKeys = 1000
        };

        var objects = new List<S3Object>();
        ListObjectsResponse response;
        do
        {
            response = await client.ListObjectsAsync(request);
            objects.AddRange(response.S3Objects.Where(entry => CheckKey(domain, entry.Key)));
            request.Marker = response.NextMarker;
        } while (response.IsTruncated);
        return objects;
    }

    private async Task<IEnumerable<S3Object>> GetS3ObjectsAsync(string domain, string path = "", bool recycle = false)
    {
        path = MakePath(domain, path) + '/';
        var s30Objects = await GetS3ObjectsByPathAsync(domain, path);
        if (string.IsNullOrEmpty(_recycleDir) || !recycle)
        {
            return s30Objects;
        }

        //s30Objects.Concat(await GetS3ObjectsByPathAsync(domain, GetRecyclePath(path)));
        return s30Objects;
    }

    public string MakePath(string domain, string path)
    {
        string result;

        path = path.TrimStart('\\', '/').TrimEnd('/').Replace('\\', '/');

        if (!string.IsNullOrEmpty(_subDir))
        {
            if (_subDir.Length == 1 && (_subDir[0] == '/' || _subDir[0] == '\\'))
            {
                result = path;
            }
            else
            {
                result = $"{_subDir}/{path}"; // Ignory all, if _subDir is not null
            }
        }
        else//Key combined from module+domain+filename
        {
            result = $"{Tenant}/{Modulename}/{domain}/{path}";
        }


        result = result.Replace("//", "/").TrimStart('/').TrimEnd('/');
        if (_lowerCasing)
        {
            result = result.ToLowerInvariant();
        }

        return result;
    }

    private string GetRecyclePath(string path)
    {
        return string.IsNullOrEmpty(_recycleDir) ? "" : $"{_recycleDir}/{path.TrimStart('/')}";
    }

    private async ValueTask RecycleAsync(IAmazonS3 client, string domain, string key)
    {
        if (string.IsNullOrEmpty(_recycleDir) || (!string.IsNullOrEmpty(domain) && domain.EndsWith("_temp")) || !_recycleUse)
        {
            return;
        }

        await CopyFileAsync(client, key, GetRecyclePath(key), domain, S3MetadataDirective.REPLACE, S3StorageClass.Glacier);
    }

    private async Task CopyFileAsync(IAmazonS3 client, string sourceKey, string destinationKey, string newdomain, S3MetadataDirective metadataDirective = S3MetadataDirective.COPY, S3StorageClass storageClass = null)
    {
        var metadataRequest = new GetObjectMetadataRequest
        {
            BucketName = _bucket,
            Key = sourceKey
        };

        var metadataResponse = await client.GetObjectMetadataAsync(metadataRequest);
        var objectSize = metadataResponse.ContentLength;

        if (objectSize >= 1000 * 1024 * 1024L) //1000 megabytes
        {
            var copyResponses = new List<CopyPartResponse>();

            var initiateRequest =
                new InitiateMultipartUploadRequest
                {
                    BucketName = _bucket,
                    Key = destinationKey,
                    CannedACL = GetDomainACL(newdomain)
                };

            if (client is not IAmazonS3Encryption)
            {
                initiateRequest.ServerSideEncryptionMethod = GetServerSideEncryptionMethod(out var kmsKeyId);
                initiateRequest.ServerSideEncryptionKeyManagementServiceKeyId = kmsKeyId;
            }

            if (storageClass != null)
            {
                initiateRequest.StorageClass = storageClass;
            }

            var initResponse = await client.InitiateMultipartUploadAsync(initiateRequest);

            var uploadId = initResponse.UploadId;

            var partSize = ChunkSize;

            var uploadTasks = new List<Task<CopyPartResponse>>();

            long bytePosition = 0;
            for (var i = 1; bytePosition < objectSize; i++)
            {
                var copyRequest = new CopyPartRequest
                {
                    DestinationBucket = _bucket,
                    DestinationKey = destinationKey,
                    SourceBucket = _bucket,
                    SourceKey = sourceKey,
                    UploadId = uploadId,
                    FirstByte = bytePosition,
                    LastByte = bytePosition + partSize - 1 >= objectSize ? objectSize - 1 : bytePosition + partSize - 1,
                    PartNumber = i
                };

                uploadTasks.Add(client.CopyPartAsync(copyRequest));

                bytePosition += partSize;
            }

            copyResponses.AddRange(await Task.WhenAll(uploadTasks));

            var completeRequest =
                new CompleteMultipartUploadRequest
                {
                    BucketName = _bucket,
                    Key = destinationKey,
                    UploadId = initResponse.UploadId
                };
            completeRequest.AddPartETags(copyResponses);

            await client.CompleteMultipartUploadAsync(completeRequest);
        }
        else
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = _bucket,
                SourceKey = sourceKey,
                DestinationBucket = _bucket,
                DestinationKey = destinationKey,
                CannedACL = GetDomainACL(newdomain),
                MetadataDirective = metadataDirective
            };

            if (client is not IAmazonS3Encryption)
            {
                request.ServerSideEncryptionMethod = GetServerSideEncryptionMethod(out var kmsKeyId);
                request.ServerSideEncryptionKeyManagementServiceKeyId = kmsKeyId;
            }

            if (storageClass != null)
            {
                request.StorageClass = storageClass;
            }

            await client.CopyObjectAsync(request);
        }
    }

    public async Task ConcatFileStreamAsync(Stream stream, string tarKey, string destinationDomain, string destinationKey, ConcurrentQueue<int> queue, CancellationToken token)
    {
        queue.TryDequeue(out var ext);
        destinationKey += ext;
        var (uploadId, eTags, partNumber) = await InitiateConcatAsync(destinationDomain, destinationKey, token: token).ConfigureAwait(false);

        using var s3 = GetClient();
        var destinationPath = MakePath(destinationDomain, destinationKey);

        const int blockSize = 512;

        long prevFileSize = 0;
        try
        {
            var objResult = await s3.GetObjectMetadataAsync(_bucket, destinationPath, token).ConfigureAwait(false);
            prevFileSize = objResult.ContentLength;
        }
        catch { }

        var header = BuilderHeaders.CreateHeader(tarKey, stream.Length);

        var ms = new MemoryStream();
        if (prevFileSize % blockSize != 0)
        {
            var endBlock = new byte[blockSize - prevFileSize % blockSize];
            await ms.WriteAsync(endBlock, token);
        }
        await ms.WriteAsync(header, token);

        stream.Position = 0; 
        await stream.CopyToAsync(ms, token);
        await stream.DisposeAsync();

        stream = ms;
        stream.Position = 0;

        var uploadRequest = new UploadPartRequest
        {
            BucketName = _bucket,
            Key = destinationPath,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = stream
        };
        eTags.Add(new PartETag(partNumber, (await s3.UploadPartAsync(uploadRequest, token).ConfigureAwait(false)).ETag));

        var completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = destinationPath,
            UploadId = uploadId,
            PartETags = eTags
        };
        await s3.CompleteMultipartUploadAsync(completeRequest, token).ConfigureAwait(false);
        await stream.DisposeAsync();
        queue.Enqueue(ext);
    }

    public async Task ConcatFileAsync(string pathFile, string destinationDomain, string destinationKey)
    {
        var (uploadId, eTags, partNumber) = await InitiateConcatAsync(destinationDomain, destinationKey);

        using var s3 = GetClient();
        var obj = await s3.GetObjectMetadataAsync(_bucket, pathFile);

        destinationKey = MakePath(destinationDomain, destinationKey);

        var objectSize = obj.ContentLength;

        var partSize = ChunkSize;
        long bytePosition = 0;

        for (var i = 1; bytePosition < objectSize; i++)
        {
            var copyRequest = new CopyPartRequest
            {
                DestinationBucket = _bucket,
                DestinationKey = destinationKey,
                SourceBucket = _bucket,
                SourceKey = pathFile,
                UploadId = uploadId,
                FirstByte = bytePosition,
                LastByte = bytePosition + partSize - 1 >= objectSize ? objectSize - 1 : bytePosition + partSize - 1,
                PartNumber = partNumber
            };
            bytePosition += partSize;
            eTags.Add(new PartETag(partNumber, (await s3.CopyPartAsync(copyRequest)).ETag));
            partNumber++;
        }

        var completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = destinationKey,
            UploadId = uploadId,
            PartETags = eTags
        };
         await s3.CompleteMultipartUploadAsync(completeRequest);
    }

    public async Task ConcatFileAsync(string pathFile, string tarKey, string destinationDomain, string destinationKey, ConcurrentQueue<int> queue, CancellationToken token)
    {
        queue.TryDequeue(out var ext);
        destinationKey += ext;
        var (uploadId, eTags, partNumber) = await InitiateConcatAsync(destinationDomain, destinationKey, token: token).ConfigureAwait(false);
        using var s3 = GetClient();
        var destinationPath = MakePath(destinationDomain, destinationKey);

        const int blockSize = 512;

        long prevFileSize = 0;
        try
        {
            var objResult = await s3.GetObjectMetadataAsync(_bucket, destinationPath, token).ConfigureAwait(false);
            prevFileSize = objResult.ContentLength;
        }
        catch{}

        var objFile = await s3.GetObjectMetadataAsync(_bucket, pathFile, token).ConfigureAwait(false);
        var header = BuilderHeaders.CreateHeader(tarKey, objFile.ContentLength);

        using var stream = new MemoryStream();
        if (prevFileSize % blockSize != 0)
        {
            var endBlock = new byte[blockSize - prevFileSize % blockSize];
            await stream.WriteAsync(endBlock, token);
        }
        
        await stream.WriteAsync(header, token);
        
        stream.Position = 0;

        var uploadRequest = new UploadPartRequest
        {
            BucketName = _bucket,
            Key = destinationPath,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = stream
        };
        eTags.Add(new PartETag(partNumber, (await s3.UploadPartAsync(uploadRequest, token).ConfigureAwait(false)).ETag));

        var completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = destinationPath,
            UploadId = uploadId,
            PartETags = eTags
        };

        await s3.CompleteMultipartUploadAsync(completeRequest, token).ConfigureAwait(false);

        /*******/
        (uploadId, eTags, partNumber) = await InitiateConcatAsync(destinationDomain, destinationKey, token: token).ConfigureAwait(false);

        var copyRequest = new CopyPartRequest
        {
            DestinationBucket = _bucket,
            DestinationKey = destinationPath,
            SourceBucket = _bucket,
            SourceKey = pathFile,
            UploadId = uploadId,
            PartNumber = partNumber
        };
        eTags.Add(new PartETag(partNumber, (await s3.CopyPartAsync(copyRequest, token).ConfigureAwait(false)).ETag));

        completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = destinationPath,
            UploadId = uploadId,
            PartETags = eTags
        };
        await s3.CompleteMultipartUploadAsync(completeRequest, token).ConfigureAwait(false);

        queue.Enqueue(ext);
    }

    public async Task AddEndAsync(string domain, string key, bool last = false)
    {
        using var s3 = GetClient();
        var path = MakePath(domain, key);
        var blockSize = 512;

        var (uploadId, eTags, partNumber) = await InitiateConcatAsync(domain, key);

        var obj = await s3.GetObjectMetadataAsync(_bucket, path);
        byte[] buffer = null;

        if (last)
        {
            buffer = new byte[blockSize - obj.ContentLength % blockSize + blockSize * 2];
        }
        else
        {
            if (obj.ContentLength % blockSize != 0)
            {
                buffer = new byte[blockSize - obj.ContentLength % blockSize];
            }
        }
        var stream = new MemoryStream();
        await stream.WriteAsync(buffer);
        stream.Position = 0;

        var uploadRequest = new UploadPartRequest
        {
            BucketName = _bucket,
            Key = path,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = stream
        };
        eTags.Add(new PartETag(partNumber, (await s3.UploadPartAsync(uploadRequest)).ETag));

        var completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = path,
            UploadId = uploadId,
            PartETags = eTags
        };

        await s3.CompleteMultipartUploadAsync(completeRequest);
    }

    public async Task ReloadFileAsync(string domain, string key, bool removeFirstBlock, bool last = false)
    {
        using var s3 = GetClient();
        var path = MakePath(domain, key);

        var (uploadId, eTags, _) = await InitiateConcatAsync(domain, key, removeFirstBlock, last);
        var completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = path,
            UploadId = uploadId,
            PartETags = eTags
        };

        await s3.CompleteMultipartUploadAsync(completeRequest);
    }

    public async Task<(string uploadId, List<PartETag> eTags, int partNumber)> InitiateConcatAsync(string domain, string key, bool removeFirstBlock = false, bool lastInit = false, CancellationToken token = default)
    {
        using var s3 = GetClient();

        key = MakePath(domain, key);

        var initiateRequest = new InitiateMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = key
        };
        var initResponse = await s3.InitiateMultipartUploadAsync(initiateRequest, token).ConfigureAwait(false);

        var eTags = new List<PartETag>();
        try
        {
            var mb5 = 5 * 1024 * 1024;
            long bytePosition = removeFirstBlock ? mb5 : 0;

            var obj = await s3.GetObjectMetadataAsync(_bucket, key, token).ConfigureAwait(false);
            var objectSize = obj.ContentLength;

            var partSize = ChunkSize;
            var partNumber = 1;
            for (var i = 1; bytePosition < objectSize; i++)
            {
                var copyRequest = new CopyPartRequest
                {
                    DestinationBucket = _bucket,
                    DestinationKey = key,
                    SourceBucket = _bucket,
                    SourceKey = key,
                    UploadId = initResponse.UploadId,
                    FirstByte = bytePosition,
                    LastByte = bytePosition + partSize - 1 >= objectSize ? objectSize - 1 : bytePosition + partSize - 1,
                    PartNumber = i
                };
                partNumber = i + 1;
                bytePosition += partSize;

                var x = objectSize - bytePosition;
                if (!lastInit && x < mb5 && x > 0)
                {
                    copyRequest.LastByte = objectSize - 1;
                    bytePosition += partSize;
                }
                eTags.Add(new PartETag(i, (await s3.CopyPartAsync(copyRequest, token).ConfigureAwait(false)).ETag));

            }
            return (initResponse.UploadId, eTags, partNumber);
        }
        catch
        {
            using var stream = new MemoryStream();
            var buffer = new byte[5 * 1024 * 1024];
            await stream.WriteAsync(buffer, token);
            stream.Position = 0;

            var uploadRequest = new UploadPartRequest
            {
                BucketName = _bucket,
                Key = key,
                UploadId = initResponse.UploadId,
                PartNumber = 1,
                InputStream = stream
            };
            eTags.Add(new PartETag(1, (await s3.UploadPartAsync(uploadRequest, token).ConfigureAwait(false)).ETag));
            return (initResponse.UploadId, eTags, 2);
        }
    }

    private AmazonCloudFrontClient GetCloudFrontClient()
    {
        var cfg = new AmazonCloudFrontConfig { MaxErrorRetry = 3 };

        return new AmazonCloudFrontClient(_accessKeyId, _secretAccessKeyId, cfg);
    }

    private IAmazonS3 GetClient()
    {
        var encryptionClient = GetEncryptionClient();

        if (encryptionClient != null)
        {
            return encryptionClient;
        }

        var cfg = new AmazonS3Config { MaxErrorRetry = 3 };

        if (!string.IsNullOrEmpty(_serviceurl))
        {
            cfg.ServiceURL = _serviceurl;

            cfg.ForcePathStyle = _forcepathstyle;
        }
        else
        {
            cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(_region);
        }

        cfg.UseHttp = _useHttp;

        return new AmazonS3Client(_accessKeyId, _secretAccessKeyId, cfg);
    }

    private class ResponseStreamWrapper(GetObjectResponse response) : Stream
    {
        public override bool CanRead => _response.ResponseStream.CanRead;
        public override bool CanSeek => _response.ResponseStream.CanSeek;
        public override bool CanWrite => _response.ResponseStream.CanWrite;
        public override long Length => _response.ContentLength;
        public override long Position
        {
            get => _response.ResponseStream.Position;
            set => _response.ResponseStream.Position = value;
        }

        private readonly GetObjectResponse _response = response ?? throw new ArgumentNullException(nameof(response));

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _response.ResponseStream.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _response.ResponseStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new())
        {
            return _response.ResponseStream.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _response.ResponseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _response.ResponseStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _response.ResponseStream.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _response.ResponseStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new())
        {
            return _response.ResponseStream.WriteAsync(buffer, cancellationToken);
        }
        
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            return _response.ResponseStream.CopyToAsync(destination, bufferSize, cancellationToken);
        }
        
        public override void Flush()
        {
            _response.ResponseStream.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _response.ResponseStream.FlushAsync(cancellationToken);
        }

        public override ValueTask DisposeAsync()
        {
            return _response?.ResponseStream?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _response.Dispose();
            }
        }
    }

    private AmazonS3EncryptionClientV2 GetEncryptionClient()
    {
        if (!string.IsNullOrEmpty(_encryptionKey))
        {
            return null;
        }

        EncryptionMaterialsV2 encryptionMaterials = null;

        switch (_encryptionMethod)
        {
            case EncryptionMethod.ClientKms:
                var encryptionContext = new Dictionary<string, string>();
                encryptionMaterials = new EncryptionMaterialsV2(_encryptionKey, KmsType.KmsContext, encryptionContext);
                break;
                //case EncryptionMethod.ClientAes:
                //    var symmetricAlgorithm = Aes.Create();
                //    symmetricAlgorithm.Key = Encoding.UTF8.GetBytes(_encryptionKey);
                //    encryptionMaterials = new EncryptionMaterialsV2(symmetricAlgorithm, SymmetricAlgorithmType.AesGcm);
                //    break;
                //case EncryptionMethod.ClientRsa:
                //    var asymmetricAlgorithm = RSA.Create();
                //    asymmetricAlgorithm.FromXmlString(_encryptionKey);
                //    encryptionMaterials = new EncryptionMaterialsV2(asymmetricAlgorithm, AsymmetricAlgorithmType.RsaOaepSha1);
                //    break;
        }

        if (encryptionMaterials == null)
        {
            return null;
        }

        var cfg = new AmazonS3CryptoConfigurationV2(SecurityProfile.V2AndLegacy)
        {
            StorageMode = CryptoStorageMode.ObjectMetadata,
            MaxErrorRetry = 3
        };

        if (!string.IsNullOrEmpty(_serviceurl))
        {
            cfg.ServiceURL = _serviceurl;
            cfg.ForcePathStyle = _forcepathstyle;
        }
        else
        {
            cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(_region);
        }

        cfg.UseHttp = _useHttp;

        return new AmazonS3EncryptionClientV2(_accessKeyId, _secretAccessKeyId, cfg, encryptionMaterials);
    }
    private ServerSideEncryptionMethod GetServerSideEncryptionMethod(out string kmsKeyId)
    {
        kmsKeyId = null;

        var method = ServerSideEncryptionMethod.None;

        switch (_encryptionMethod)
        {
            case EncryptionMethod.ServerS3:
                method = ServerSideEncryptionMethod.AES256;
                break;
            case EncryptionMethod.ServerKms:
                method = ServerSideEncryptionMethod.AWSKMS;
                if (!string.IsNullOrEmpty(_encryptionKey))
                {
                    kmsKeyId = _encryptionKey;
                }
                break;
        }

        return method;
    }

    public override async Task<string> GetFileEtagAsync(string domain, string path)
    {
        using var client = GetClient();

        var getObjectMetadataRequest = new GetObjectMetadataRequest
        {
            BucketName = _bucket,
            Key = MakePath(domain, path)
        };

        var el = await client.GetObjectMetadataAsync(getObjectMetadataRequest);

        return el.ETag;
    }

    private enum EncryptionMethod
    {
        None,
        ServerS3,
        ServerKms,
        ClientKms
        //ClientAes,
        //ClientRsa
    }
}
