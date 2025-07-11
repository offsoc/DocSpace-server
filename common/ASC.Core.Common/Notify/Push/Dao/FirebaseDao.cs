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

namespace ASC.Core.Common.Notify.Push;

[Scope]
public class FirebaseDao(IDbContextFactory<FirebaseDbContext> dbContextFactory)
{
    public async Task<FireBaseUser> RegisterUserDeviceAsync(Guid userId, int tenantId, string fbDeviceToken, bool isSubscribed, string application)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var user = await Queries.FireBaseUserAsync(dbContext, tenantId, userId, application, fbDeviceToken);


        if (user == null)
        {
            var newUser = new FireBaseUser
            {
                UserId = userId,
                TenantId = tenantId,
                FirebaseDeviceToken = fbDeviceToken,
                IsSubscribed = isSubscribed,
                Application = application
            };
            await dbContext.AddAsync(newUser);
            await dbContext.SaveChangesAsync();

            return newUser;
        }

        return user;
    }

    public virtual async Task<List<FireBaseUser>> GetSubscribedUserDeviceTokensAsync(Guid userId, int tenantId, string application)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await Queries.FireBaseSubscribedUsersAsync(dbContext, tenantId, userId, application).ToListAsync();
    }

    public async Task<FireBaseUser> UpdateUserAsync(Guid userId, int tenantId, string fbDeviceToken, bool isSubscribed, string application)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        var user = await Queries.FireBaseUserAsync(dbContext, tenantId, userId, application, fbDeviceToken);

        if (user != null)
        {
            user.IsSubscribed = isSubscribed;
            dbContext.Update(user);
            await dbContext.SaveChangesAsync();
        }

        return user;
    }

    public async Task DeleteInvalidTokenAsync(Guid userId, int tenantId, string fbDeviceToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var user = await Queries.DeleteFireBaseUserTokenAsync(dbContext, tenantId, userId, fbDeviceToken);
    }

}

[Scope]
public class CacheFirebaseDao(IDbContextFactory<FirebaseDbContext> dbContextFactory) : FirebaseDao(dbContextFactory)
{
    private readonly ConcurrentDictionary<(int, Guid, string), List<FireBaseUser>> _cache = new();
    public override async Task<List<FireBaseUser>> GetSubscribedUserDeviceTokensAsync(Guid userId, int tenantId, string application)
    {

        if (!_cache.TryGetValue((tenantId, userId, application), out var result))
        {
            result = await base.GetSubscribedUserDeviceTokensAsync(userId, tenantId, application);
            _cache.TryAdd((tenantId, userId, application), result);
        }
        return result;
    }
}

static file class Queries
{
    public static readonly Func<FirebaseDbContext, int, Guid, string, string, Task<FireBaseUser>> FireBaseUserAsync =
        Microsoft.EntityFrameworkCore.EF.CompileAsyncQuery(
            (FirebaseDbContext ctx, int tenantId, Guid userId, string application, string fbDeviceToken) =>
                ctx.Users.FirstOrDefault(r =>  r.UserId == userId && r.TenantId == tenantId && r.Application == application && r.FirebaseDeviceToken == fbDeviceToken));

    public static readonly Func<FirebaseDbContext, int, Guid, string, IAsyncEnumerable<FireBaseUser>>
        FireBaseSubscribedUsersAsync = Microsoft.EntityFrameworkCore.EF.CompileAsyncQuery(
            (FirebaseDbContext ctx, int tenantId, Guid userId, string application) =>
                ctx.Users
                    
                    .Where(r => r.UserId == userId)
                    .Where(r => r.TenantId == tenantId)
                    .Where(r => r.IsSubscribed == true)
                    .Where(r => r.Application == application));

    public static readonly Func<FirebaseDbContext, int, Guid, string, Task<int>> DeleteFireBaseUserTokenAsync =
        Microsoft.EntityFrameworkCore.EF.CompileAsyncQuery(
            (FirebaseDbContext ctx, int tenantId, Guid userId, string fbDeviceToken) =>
                ctx.Users
                    .Where(r => r.TenantId == tenantId)
                    .Where(r => r.UserId == userId)
                    .Where(r => r.FirebaseDeviceToken == fbDeviceToken)
                    .ExecuteDelete());
}