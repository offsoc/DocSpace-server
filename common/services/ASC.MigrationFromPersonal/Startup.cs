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

using System.Threading.Channels;

using ASC.Api.Core;
using ASC.Api.Core.Core;
using ASC.Common;
using ASC.Common.Logging;
using ASC.Core.Common.EF;
using ASC.Core.Common.EF.Context;
using ASC.Core.Common.Hosting;
using ASC.Core.Common.Notify.Engine;
using ASC.Core.Common.Quota;
using ASC.Core.Notify.Socket;
using ASC.Data.Backup.EF.Context;
using ASC.Data.Storage;
using ASC.EventBus.Abstractions;
using ASC.EventBus.Extensions.Logger;
using ASC.Feed.Context;
using ASC.Files.Core.EF;
using ASC.Files.Core.Security;
using ASC.Files.Core.VirtualRooms;
using ASC.MessagingSystem.EF.Context;
using ASC.MigrationFromPersonal.Core;
using ASC.MigrationFromPersonal.EF;
using ASC.Notify.Engine;
using ASC.Webhooks.Core.EF.Context;

using StackExchange.Redis.Extensions.Core.Configuration;

namespace ASC.MigrationFromPersonal;

public class Startup
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly DIHelper _diHelper;

    public Startup(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _diHelper = new DIHelper();
    }

    public async Task ConfigureServicesAsync(IServiceCollection services)
    {
        var connectionMultiplexer = await services.GetRedisConnectionMultiplexerAsync(_configuration, GetType().Namespace);

        services.RegisterFeature()
             .AddScoped<EFLoggerFactory>()
             .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
             .AddHttpClient()
             .AddBaseDbContextPool<AccountLinkContext>()
             .AddBaseDbContextPool<BackupsContext>()
             .AddBaseDbContextPool<FilesDbContext>()
             .AddBaseDbContextPool<CoreDbContext>()
             .AddBaseDbContextPool<TenantDbContext>()
             .AddBaseDbContextPool<UserDbContext>()
             .AddBaseDbContextPool<TelegramDbContext>()
             .AddBaseDbContextPool<CustomDbContext>()
             .AddBaseDbContextPool<WebstudioDbContext>()
             .AddBaseDbContextPool<InstanceRegistrationContext>()
             .AddBaseDbContextPool<IntegrationEventLogContext>()
             .AddBaseDbContextPool<FeedDbContext>()
             .AddBaseDbContextPool<MessagesContext>()
             .AddBaseDbContextPool<WebhooksDbContext>()
             .AddBaseDbContextPool<MigrationContext>()
             .AddAutoMapper(BaseStartup.GetAutoMapperProfileAssemblies())
             .AddMemoryCache()
             .AddCacheNotify(_configuration)
             .AddDistributedCache(connectionMultiplexer)
             .AddDistributedLock(_configuration)
             .AddEventBus(_configuration);

        var redisConfiguration = _configuration.GetSection("Redis").Get<RedisConfiguration>();
        var configurationOption = redisConfiguration?.ConfigurationOptions;
        configurationOption.ClientName = "migration to docspace";
        var redisConnection = await RedisPersistentConnection.InitializeAsync(configurationOption);
        services.AddSingleton(redisConfiguration)
                .AddSingleton(redisConnection);

        services.AddSingleton(Channel.CreateUnbounded<NotifyRequest>());
        services.AddSingleton(svc => svc.GetRequiredService<Channel<NotifyRequest>>().Reader);
        services.AddSingleton(svc => svc.GetRequiredService<Channel<NotifyRequest>>().Writer);
        services.AddHostedService<NotifySenderService>();

        services.AddSingleton(Channel.CreateUnbounded<SocketData>());
        services.AddSingleton(svc => svc.GetRequiredService<Channel<SocketData>>().Reader);
        services.AddSingleton(svc => svc.GetRequiredService<Channel<SocketData>>().Writer);
        services.AddHostedService<SocketService>();

        _diHelper.Configure(services);

        _diHelper.TryAdd<MigrationCreator>();
        _diHelper.TryAdd<MigrationRunner>();
        _diHelper.TryAdd<MigrationService>();
        _diHelper.TryAdd<QuotaSocketManager>();
        _diHelper.TryAdd<RoomLogoValidator>();
        _diHelper.TryAdd<FileValidator>();
        _diHelper.TryAdd<FileSecurity>();

        services.AddHostedService<MigrationService>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {

    }
}