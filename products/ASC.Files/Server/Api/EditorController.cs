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

using ASC.Web.Api.Core;

namespace ASC.Files.Api;

[ConstraintRoute("int")]
[DefaultRoute("file")]
public class EditorControllerInternal(FileStorageService fileStorageService,
        DocumentServiceHelper documentServiceHelper,
        EncryptionKeyPairDtoHelper encryptionKeyPairDtoHelper,
        SettingsManager settingsManager,
        EntryManager entryManager,
        FolderDtoHelper folderDtoHelper,
        FileDtoHelper fileDtoHelper,
        ExternalShare externalShare,
        AuthContext authContext,
        ConfigurationConverter<int> configurationConverter,
        SecurityContext securityContext)
        : EditorController<int>(fileStorageService, documentServiceHelper, encryptionKeyPairDtoHelper, settingsManager, entryManager, folderDtoHelper, fileDtoHelper, externalShare, authContext, configurationConverter, securityContext);

[DefaultRoute("file")]
public class EditorControllerThirdparty(FileStorageService fileStorageService,
        DocumentServiceHelper documentServiceHelper,
        EncryptionKeyPairDtoHelper encryptionKeyPairDtoHelper,
        SettingsManager settingsManager,
        EntryManager entryManager,
        FolderDtoHelper folderDtoHelper,
        FileDtoHelper fileDtoHelper,
        ExternalShare externalShare,
        AuthContext authContext,
        ConfigurationConverter<string> configurationConverter,
        SecurityContext securityContext)
        : EditorController<string>(fileStorageService, documentServiceHelper, encryptionKeyPairDtoHelper, settingsManager, entryManager, folderDtoHelper, fileDtoHelper, externalShare, authContext, configurationConverter, securityContext);

public abstract class EditorController<T>(FileStorageService fileStorageService,
        DocumentServiceHelper documentServiceHelper,
        EncryptionKeyPairDtoHelper encryptionKeyPairDtoHelper,
        SettingsManager settingsManager,
        EntryManager entryManager,
        FolderDtoHelper folderDtoHelper,
        FileDtoHelper fileDtoHelper,
        ExternalShare externalShare,
        AuthContext authContext,
        ConfigurationConverter<T> configurationConverter,
        SecurityContext securityContext)
    : ApiControllerBase(folderDtoHelper, fileDtoHelper)
{

    /// <summary>
    /// Saves edits to a file with the ID specified in the request.
    /// </summary>
    /// <short>Save file edits</short>
    /// <path>api/2.0/files/file/{fileId}/saveediting</path>
    [Tags("Files / Files")]
    [SwaggerResponse(200, "Saved file parameters", typeof(FileDto<int>))]
    [SwaggerResponse(400, "No file id or folder id toFolderId determine provider")]
    [SwaggerResponse(403, "You do not have enough permissions to edit the file")]
    [HttpPut("{fileId}/saveediting")]
    public async Task<FileDto<T>> SaveEditingFromFormAsync(SaveEditingRequestDto<T> inDto)
    {
        return await _fileDtoHelper.GetAsync(await fileStorageService.SaveEditingAsync(inDto.FileId, inDto.FileExtension, inDto.DownloadUri, inDto.File?.OpenReadStream(), inDto.Forcesave));
    }

    /// <summary>
    /// Informs about opening a file with the ID specified in the request for editing, locking it from being deleted or moved (this method is called by the mobile editors).
    /// </summary>
    /// <short>Start file editing</short>
    /// <path>api/2.0/files/file/{fileId}/startedit</path>
    /// <requiresAuthorization>false</requiresAuthorization>
    [Tags("Files / Files")]
    [SwaggerResponse(200, "File key for Document Service", typeof(string))]
    [SwaggerResponse(403, "You don't have enough permission to view the file")]
    [AllowAnonymous]
    [HttpPost("{fileId}/startedit")]
    public async Task<string> StartEditAsync(StartEditRequestDto<T> inDto)
    {
        return await fileStorageService.StartEditAsync(inDto.FileId, inDto.File.EditingAlone);
    }

    /// <summary>
    /// Starts filling a file with the ID specified in the request.
    /// </summary>
    /// <short>Start file filling</short>
    /// <path>api/2.0/files/file/{fileId}/startfilling</path>
    [Tags("Files / Files")]
    [SwaggerResponse(200, "File information", typeof(FileDto<int>))]
    [SwaggerResponse(403, "You do not have enough permissions to edit the file")]
    [HttpPut("{fileId}/startfilling")]
    public async Task<FileDto<T>> StartFillingAsync(StartFillingRequestDto<T> inDto)
    {
        var file = await fileStorageService.StartFillingAsync(inDto.FileId);

        return await _fileDtoHelper.GetAsync(file);
    }

    /// <summary>
    /// Tracks file changes when editing.
    /// </summary>
    /// <short>Track file editing</short>
    /// <path>api/2.0/files/file/{fileId}/trackeditfile</path>
    /// <requiresAuthorization>false</requiresAuthorization>
    [Tags("Files / Files")]
    [SwaggerResponse(200, "File changes", typeof(KeyValuePair<bool, string>))]
    [SwaggerResponse(403, "You don't have enough permission to perform the operation")]
    [AllowAnonymous]
    [HttpGet("{fileId}/trackeditfile")]
    public async Task<KeyValuePair<bool, string>> TrackEditFileAsync(TrackEditFileRequestDto<T> inDto)
    {
        return await fileStorageService.TrackEditFileAsync(inDto.FileId, inDto.TabId, inDto.DocKeyForTrack, inDto.IsFinish);
    }

    /// <summary>
    /// Returns the initialization configuration of a file to open it in the editor.
    /// </summary>
    /// <short>Open a file configuration</short>
    /// <path>api/2.0/files/file/{fileId}/openedit</path>
    /// <requiresAuthorization>false</requiresAuthorization>
    [Tags("Files / Files")]
    [SwaggerResponse(200, "Configuration parameters", typeof(ConfigurationDto<int>))]
    [SwaggerResponse(403, "You don't have enough permission to view the file")]
    [AllowAnonymous]
    [AllowNotPayment]
    [HttpGet("{fileId}/openedit")]
    public async Task<ConfigurationDto<T>> OpenEditAsync(OpenEditRequestDto<T> inDto)
    {
        var (file, lastVersion) = await documentServiceHelper.GetCurFileInfoAsync(inDto.FileId, inDto.Version);
        FormOpenSetup<T> formOpenSetup = null;

        var rootFolder = await documentServiceHelper.GetRootFolderAsync(file);
        if (file.IsForm && rootFolder.RootFolderType != FolderType.RoomTemplates)
        {

            formOpenSetup = rootFolder.FolderType switch
            {
                FolderType.FillingFormsRoom => await documentServiceHelper.GetFormOpenSetupForFillingRoomAsync(file, rootFolder, inDto.EditorType, inDto.Edit, entryManager),
                FolderType.FormFillingFolderInProgress => documentServiceHelper.GetFormOpenSetupForFolderInProgress(file, inDto.EditorType),
                FolderType.FormFillingFolderDone => documentServiceHelper.GetFormOpenSetupForFolderDone<T>(inDto.EditorType),
                FolderType.VirtualDataRoom => await documentServiceHelper.GetFormOpenSetupForVirtualDataRoomAsync(file, inDto.EditorType),
                FolderType.USER => await documentServiceHelper.GetFormOpenSetupForUserFolderAsync(file, inDto.EditorType, inDto.Edit, inDto.Fill),
                _ => new FormOpenSetup<T>
                {
                    CanEdit = !inDto.Fill,
                    CanFill = inDto.Fill,
                }
            };
            formOpenSetup.RootFolder = rootFolder;
        }

        var docParams = await documentServiceHelper.GetParamsAsync(
            formOpenSetup is { Draft: not null } ? formOpenSetup.Draft : file, 
            lastVersion,
            formOpenSetup?.CanEdit ?? !file.IsCompletedForm,
            !inDto.View, 
            true, formOpenSetup == null || formOpenSetup.CanFill,
            formOpenSetup?.EditorType ?? inDto.EditorType,
            formOpenSetup is { IsSubmitOnly: true });

        var configuration = docParams.Configuration;
        file = docParams.File;

        if (file.RootFolderType == FolderType.Privacy && await PrivacyRoomSettings.GetEnabledAsync(settingsManager) || docParams.LocatedInPrivateRoom)
        {
            var keyPair = await encryptionKeyPairDtoHelper.GetKeyPairAsync();
            if (keyPair != null)
            {
                configuration.EditorConfig.EncryptionKeys = new EncryptionKeysConfig
                {
                    PrivateKeyEnc = keyPair.PrivateKeyEnc,
                    PublicKey = keyPair.PublicKey
                };
            }
        }

        var result = await configurationConverter.Convert(configuration, file);

        if (authContext.IsAuthenticated && !file.Encrypted && !file.ProviderEntry 
            && result.File.Security.TryGetValue(FileSecurity.FilesSecurityActions.Read, out var canRead) && canRead)
        {
            var linkId = await externalShare.GetLinkIdAsync();

            if (linkId != Guid.Empty && file.RootFolderType == FolderType.USER && file.CreateBy != authContext.CurrentAccount.ID)
            {
                await entryManager.MarkFileAsRecentByLink(file, linkId);
            }
            else
            {
                await entryManager.MarkAsRecent(file);
            }
        }
        
        if (formOpenSetup != null)
        {

            if (formOpenSetup.RootFolder.FolderType is FolderType.VirtualDataRoom)
            {
                result.StartFilling = file.Security[FileSecurity.FilesSecurityActions.StartFilling];
                result.StartFillingMode = StartFillingMode.StartFilling;
                result.Document.ReferenceData.RoomId = formOpenSetup.RootFolder.Id.ToString();

                result.EditorConfig.Customization.StartFillingForm = new StartFillingForm { Text = FilesCommonResource.StartFillingModeEnum_StartFilling };
                if (!string.IsNullOrEmpty(formOpenSetup.RoleName))
                {
                    result.EditorConfig.User.Roles = [formOpenSetup.RoleName];
                    result.FillingStatus = true;
                }
            }
            else
            {
                if (result.Document.Permissions.Copy && !securityContext.CurrentAccount.ID.Equals(ASC.Core.Configuration.Constants.Guest.ID))
                {
                    result.StartFillingMode = StartFillingMode.ShareToFillOut;
                    result.StartFilling = formOpenSetup.CanStartFilling;
                    result.EditorConfig.Customization.StartFillingForm = new StartFillingForm { Text = FilesCommonResource.StartFillingModeEnum_ShareToFillOut };
                }
            }

        }

        if (!string.IsNullOrEmpty(formOpenSetup?.FillingSessionId))
        {
            result.FillingSessionId = formOpenSetup.FillingSessionId;
            if (securityContext.CurrentAccount.ID.Equals(ASC.Core.Configuration.Constants.Guest.ID))
            {
                result.EditorConfig.User = new UserConfig
                {
                    Id = formOpenSetup.FillingSessionId
                };
            }
        }

        if (rootFolder.RootFolderType == FolderType.RoomTemplates)
        {
            result.File.CanShare = false;
        }
        return result;
    }

    /// <summary>
    /// Returns a link to download a file with the ID specified in the request asynchronously.
    /// </summary>
    /// <short>Get file download link asynchronously</short>
    /// <path>api/2.0/files/file/{fileId}/presigned</path>
    [Tags("Files / Files")]
    [SwaggerResponse(200, "File download link", typeof(DocumentService.FileLink))]
    [HttpGet("{fileId}/presigned")]
    public async Task<DocumentService.FileLink> GetPresignedFileUriAsync(FileIdRequestDto<T> inDto)
    {
        return await fileStorageService.GetPresignedUriAsync(inDto.FileId);
    }

    /// <summary>
    /// Returns a list of users with their access rights to the file with the ID specified in the request.
    /// </summary>
    /// <short>Get user access rights by file ID</short>
    /// <path>api/2.0/files/file/{fileId}/sharedusers</path>
    /// <collection>list</collection>
    [Tags("Files / Sharing")]
    [SwaggerResponse(200, "List of users with their access rights to the file", typeof(List<MentionWrapper>))]
    [HttpGet("{fileId}/sharedusers")]
    public async Task<List<MentionWrapper>> SharedUsers(FileIdRequestDto<T> inDto)
    {
        return await fileStorageService.SharedUsersAsync(inDto.FileId);
    }

    /// <summary>
    /// Returns a list of users with their access rights to the file.
    /// </summary>
    /// <short>Get user access rights</short>
    /// <path>api/2.0/files/infousers</path>
    /// <collection>list</collection>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Tags("Files / Files")]
    [SwaggerResponse(200, "List of users with their access rights to the file", typeof(List<MentionWrapper>))]
    [HttpPost("infousers")]
    public async Task<List<MentionWrapper>> GetInfoUsers(GetInfoUsersRequestDto inDto)
    {
        return await fileStorageService.GetInfoUsersAsync(inDto.UserIds);
    }

    /// <summary>
    /// Returns the reference data to uniquely identify a file in its system and check the availability of insering data into the destination spreadsheet by the external link.
    /// </summary>
    /// <short>Get reference data</short>
    /// <path>api/2.0/files/file/referencedata</path>
    [Tags("Files / Files")]
    [SwaggerResponse(200, "File reference data", typeof(FileReference))]
    [HttpPost("referencedata")]
    public async Task<FileReference> GetReferenceDataAsync(GetReferenceDataDto<T> inDto)
    {
        return await fileStorageService.GetReferenceDataAsync(inDto.FileKey, inDto.InstanceId, inDto.SourceFileId, inDto.Path, inDto.Link);
    }

    /// <summary>
    /// Returns a list of users with their access rights to the protected file with the ID specified in the request.
    /// </summary>
    /// <short>Get users access rights to the protected file</short>
    /// <path>api/2.0/files/file/{fileId}/protectusers</path>
    /// <collection>list</collection>
    [Tags("Files / Files")]
    [SwaggerResponse(200, "List of users with their access rights to the protected file", typeof(List<MentionWrapper>))]
    [HttpGet("{fileId}/protectusers")]
    public async Task<List<MentionWrapper>> ProtectUsers(FileIdRequestDto<T> inDto)
    {
        return await fileStorageService.ProtectUsersAsync(inDto.FileId);
    }
}

public class EditorController(FilesLinkUtility filesLinkUtility,
        MessageService messageService,
        DocumentServiceConnector documentServiceConnector,
        CommonLinkUtility commonLinkUtility,
        FolderDtoHelper folderDtoHelper,
        FileDtoHelper fileDtoHelper,
        CspSettingsHelper cspSettingsHelper,
        PermissionContext permissionContext)
    : ApiControllerBase(folderDtoHelper, fileDtoHelper)
{
    /// <summary>
    /// Checks the document service location URL.
    /// </summary>
    /// <short>Check the document service URL</short>
    /// <path>api/2.0/files/docservice</path>
    [Tags("Files / Settings")]
    [SwaggerResponse(200, "Document service information: the Document Server address, the Document Server address in the local private network, the Community Server address", typeof(DocServiceUrlDto))]
    [SwaggerResponse(400, "Invalid input urls/Mixed Active Content is not allowed. HTTPS address for Document Server is required")]
    //[SwaggerResponse(503, "Unable to establish a connection with the Document Server")]
    [HttpPut("docservice")]
    public async Task<DocServiceUrlDto> CheckDocServiceUrl(CheckDocServiceUrlRequestDto inDto)
    {
        await permissionContext.DemandPermissionsAsync(SecurityConstants.EditPortalSettings);

        var currentDocServiceUrl = filesLinkUtility.GetDocServiceUrl();
        var currentDocServiceUrlInternal = filesLinkUtility.GetDocServiceUrlInternal();
        var currentDocServicePortalUrl = filesLinkUtility.GetDocServicePortalUrl();
        var currentDocServiceSecretValue = await filesLinkUtility.GetDocServiceSignatureSecretAsync();
        var currentDocServiceSecretHeader = await filesLinkUtility.GetDocServiceSignatureHeaderAsync();
        var currentDocServiceSslVerification = await filesLinkUtility.GetDocServiceSslVerificationAsync();

        if (!ValidateUrl(inDto.DocServiceUrl) ||
            !ValidateUrl(inDto.DocServiceUrlInternal) ||
            !ValidateUrl(inDto.DocServiceUrlPortal))
        {
            throw new Exception("Invalid input urls");
        }

        if (!string.IsNullOrEmpty(inDto.DocServiceSignatureSecret) &&
            string.IsNullOrEmpty(inDto.DocServiceSignatureHeader))
        {
            throw new Exception("Invalid signature header");
        }

        await filesLinkUtility.SetDocServiceUrlAsync(inDto.DocServiceUrl);
        await filesLinkUtility.SetDocServiceUrlInternalAsync(inDto.DocServiceUrlInternal);
        await filesLinkUtility.SetDocServicePortalUrlAsync(inDto.DocServiceUrlPortal);
        await filesLinkUtility.SetDocServiceSignatureSecretAsync(inDto.DocServiceSignatureSecret);
        await filesLinkUtility.SetDocServiceSignatureHeaderAsync(inDto.DocServiceSignatureHeader);
        await filesLinkUtility.SetDocServiceSslVerificationAsync(inDto.DocServiceSslVerification ?? true);

        var https = new Regex(@"^https://", RegexOptions.IgnoreCase);
        var http = new Regex(@"^http://", RegexOptions.IgnoreCase);
        if (https.IsMatch(commonLinkUtility.GetFullAbsolutePath("")) && http.IsMatch(filesLinkUtility.GetDocServiceUrl()))
        {
            throw new Exception("Mixed Active Content is not allowed. HTTPS address for Document Server is required.");
        }

        try
        {
            await documentServiceConnector.CheckDocServiceUrlAsync();

            messageService.Send(MessageAction.DocumentServiceLocationSetting);

            var settings = await cspSettingsHelper.LoadAsync();

            _ = await cspSettingsHelper.SaveAsync(settings.Domains ?? []);
        }
        catch (Exception)
        {
            await filesLinkUtility.SetDocServiceUrlAsync(currentDocServiceUrl);
            await filesLinkUtility.SetDocServiceUrlInternalAsync(currentDocServiceUrlInternal);
            await filesLinkUtility.SetDocServicePortalUrlAsync(currentDocServicePortalUrl);
            await filesLinkUtility.SetDocServiceSignatureSecretAsync(currentDocServiceSecretValue);
            await filesLinkUtility.SetDocServiceSignatureHeaderAsync(currentDocServiceSecretHeader);
            await filesLinkUtility.SetDocServiceSslVerificationAsync(currentDocServiceSslVerification);

            throw new Exception("Unable to establish a connection with the Document Server.");
        }
        var version = new DocServiceUrlRequestDto { Version = false };
        return await GetDocServiceUrlAsync(version);

        bool ValidateUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return true;
            }

            var success = Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri);

            if (uri == null || uri.IsAbsoluteUri && !String.IsNullOrEmpty(uri.Query))
            {
                return false;
            }

            return success;
        }
    }

    /// <summary>
    /// Returns the URL address of the connected editors.
    /// </summary>
    /// <short>Get the document service URL</short>
    /// <path>api/2.0/files/docservice</path>
    /// <requiresAuthorization>false</requiresAuthorization>
    [Tags("Files / Settings")]
    [SwaggerResponse(200, "The document service URL with the editor version specified", typeof(DocServiceUrlDto))]
    [AllowAnonymous]
    [HttpGet("docservice")]
    public async Task<DocServiceUrlDto> GetDocServiceUrlAsync(DocServiceUrlRequestDto inDto)
    {
        var url = commonLinkUtility.GetFullAbsolutePath(filesLinkUtility.DocServiceApiUrl);

        var dsVersion = "";

        if (inDto.Version)
        {
            dsVersion = await documentServiceConnector.GetVersionAsync();
        }

        return new DocServiceUrlDto
        {
            Version = dsVersion,
            DocServiceUrlApi = url,
            DocServiceUrl = filesLinkUtility.GetDocServiceUrl(),
            DocServiceUrlInternal =filesLinkUtility.GetDocServiceUrlInternal(),
            DocServicePortalUrl = filesLinkUtility.GetDocServicePortalUrl(),
            DocServiceSignatureHeader = await filesLinkUtility.GetDocServiceSignatureHeaderAsync(),
            DocServiceSslVerification = await filesLinkUtility.GetDocServiceSslVerificationAsync(),
            IsDefault = await filesLinkUtility.IsDefaultAsync()
        };
    }
}