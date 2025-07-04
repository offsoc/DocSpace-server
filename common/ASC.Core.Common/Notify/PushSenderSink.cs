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

using ASC.Core.Common.Notify.Push.Dao;

using Constants = ASC.Core.Configuration.Constants;

namespace ASC.Core.Common.Notify;

public class PushSenderSink(INotifySender sender) : Sink
{
    private static readonly string _senderName = Constants.NotifyPushSenderSysName;
    private readonly INotifySender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    public override async Task<SendResponse> ProcessMessage(INoticeMessage message, IServiceScope scope)
    {
        try
        {

            var result = SendResult.OK;
            var m = await scope.ServiceProvider.GetRequiredService<PushSenderSinkMessageCreator>().CreateNotifyMessage(message, _senderName);
            if (string.IsNullOrEmpty(m.Reciever))
            {
                result = SendResult.IncorrectRecipient;
            }
            else
            {
                await _sender.SendAsync(m);
            }

            return new SendResponse(message, Constants.NotifyPushSenderSysName, result);
        }
        catch (Exception error)
        {
            return new SendResponse(message, Constants.NotifyPushSenderSysName, error);
        }
    }

    private T GetTagValue<T>(INoticeMessage message, string tagName)
    {
        var tag = message.Arguments.FirstOrDefault(arg => arg.Tag == tagName);

        return tag != null ? (T)tag.Value : default;
    }
}
public class LowerCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) =>
        name.ToLower();
}
[Scope]
public class PushSenderSinkMessageCreator(UserManager userManager, TenantManager tenantManager, CoreSettings coreSettings) : SinkMessageCreator
{
    public override async Task<NotifyMessage> CreateNotifyMessage(INoticeMessage message, string senderName)
    {
        var tenant = tenantManager.GetCurrentTenant(false);
        if (tenant == null)
        {
            await tenantManager.SetCurrentTenantAsync(Tenant.DefaultTenant);
            tenant = tenantManager.GetCurrentTenant(false);
        }      

        var user = await userManager.GetUsersAsync(new Guid(message.Recipient.ID));
        var username = user.UserName;

        var fromTag = message.Arguments.FirstOrDefault(x => x.Tag.Equals("MessageFrom"));
        var productID = message.Arguments.FirstOrDefault(x => x.Tag.Equals("__ProductID"));
        var originalUrl = message.Arguments.FirstOrDefault(x => x.Tag.Equals("DocumentURL"));

        var folderId = message.Arguments.FirstOrDefault(x => x.Tag.Equals("FolderID"));
        var rootFolderId = message.Arguments.FirstOrDefault(x => x.Tag.Equals("FolderParentId"));
        var rootFolderType = message.Arguments.FirstOrDefault(x => x.Tag.Equals("FolderRootFolderType"));


        var notifyData = new NotifyData
        {
            Email = user.Email,
            Portal = tenant.GetTenantDomain(coreSettings, true),
            OriginalUrl = originalUrl is { Value: not null } ? originalUrl.Value.ToString() : "",
            Folder = new NotifyFolderData
            {
                Id = folderId is { Value: not null } ? folderId.Value.ToString() : "",
                ParentId = rootFolderId is { Value: not null } ? rootFolderId.Value.ToString() : "",
                RootFolderType = rootFolderType is { Value: not null } ? (int)rootFolderType.Value : 0
            }
        };

        var msg = (NoticeMessage)message;

        if (msg.ObjectID.StartsWith("file_"))
        {
            var documentTitle = message.Arguments.FirstOrDefault(x => x.Tag.Equals("DocumentTitle"));
            var documentExtension = message.Arguments.FirstOrDefault(x => x.Tag.Equals("DocumentExtension"));

            notifyData.File = new NotifyFileData
            {
                Id = msg.ObjectID[5..],
                Title = documentTitle is { Value: not null } ? documentTitle.Value.ToString() : "",
                Extension = documentExtension is { Value: not null } ? documentExtension.Value.ToString() : ""

            };
        }

        var serializeOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        var jsonNotifyData = JsonSerializer.Serialize(notifyData, serializeOptions);

        var m = new NotifyMessage
        {
            TenantId = tenant.Id,
            Reciever = username,
            Subject = fromTag is { Value: not null } ? fromTag.Value.ToString() : message.Subject,
            ContentType = message.ContentType,
            Content = message.Body,
            Sender = Constants.NotifyPushSenderSysName,
            CreationDate = DateTime.UtcNow,
            ProductID = fromTag is { Value: not null } ? productID.Value.ToString() : null,
            Data = jsonNotifyData
        };


        return m;
    }
}
