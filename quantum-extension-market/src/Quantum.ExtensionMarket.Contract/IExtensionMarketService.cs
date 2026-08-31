using System.ComponentModel;
using NOF.Contract;

namespace Quantum.ExtensionMarket.Contract;

[TransportOverHttp(HttpRpcStyle.JsonRpc, "/rpc")]
public interface IExtensionMarketService : IRpcService
{
    [Summary("Register a developer account")]
    [Category("Users")]
    Result<UserSummary> RegisterUser(RegisterUserRequest request);

    [Summary("Exchange email and password for a bearer token")]
    [Category("Users")]
    Result<LoginResponse> Login(LoginRequest request);

    [Summary("Get the authenticated user's profile")]
    [Category("Users")]
    Result<UserSummary> GetCurrentUser(EmptyRequest request);

    [Summary("Get a user profile (self or admin)")]
    [Category("Users")]
    Result<UserSummary> GetUser(GetUserRequest request);

    [Summary("Update the authenticated user's profile")]
    [Category("Users")]
    Result<UserSummary> UpdateCurrentUser(UpdateCurrentUserRequest request);

    [Summary("Update a user profile (self or admin)")]
    [Category("Users")]
    Result<UserSummary> UpdateUser(UpdateUserRequest request);

    [Summary("List registered users (admin only)")]
    [Category("Users")]
    Result<UserSummary[]> ListUsers(EmptyRequest request);

    [Summary("Replace a user's roles (admin only)")]
    [Category("Users")]
    Result<UserSummary> SetUserRoles(SetUserRolesRequest request);

    [Summary("Delete a user with no owned plugins (admin only)")]
    [Category("Users")]
    Result DeleteUser(DeleteUserRequest request);

    [Summary("List or search published plugins")]
    [Category("Plugins")]
    Result<PluginSummary[]> ListPlugins(ListPluginsRequest request);

    [Summary("Get a published plugin and its releases")]
    [Category("Plugins")]
    Result<PluginDetails> GetPlugin(GetPluginRequest request);

    [Summary("Create a plugin listing (developer or admin)")]
    [Category("Plugins")]
    Result<PluginSummary> CreatePlugin(CreatePluginRequest request);

    [Summary("Update an owned plugin listing")]
    [Category("Plugins")]
    Result<PluginSummary> UpdatePlugin(UpdatePluginRequest request);

    [Summary("Delete an owned plugin and all of its packages")]
    [Category("Plugins")]
    Result DeletePlugin(DeletePluginRequest request);

    [Summary("List releases visible to the current caller")]
    [Category("Releases")]
    Result<PluginReleaseSummary[]> ListPluginReleases(ListPluginReleasesRequest request);

    [Summary("Upload a validated plugin ZIP as a pending release")]
    [Category("Releases")]
    Result<PluginReleaseSummary> UploadPluginRelease(UploadPluginReleaseRequest request);

    [Summary("Download a published plugin ZIP")]
    [Category("Releases")]
    Result<DownloadPluginReleaseResponse> DownloadPluginRelease(DownloadPluginReleaseRequest request);

    [Summary("Check the latest published release against a Quantum version")]
    [Category("Releases")]
    Result<CompatibilityResponse> CheckCompatibility(CheckCompatibilityRequest request);

    [Summary("Publish or reject a pending release (reviewer or admin)")]
    [Category("Review")]
    Result<PluginReleaseSummary> ReviewPluginRelease(ReviewPluginReleaseRequest request);

    [Summary("List all releases for moderation (reviewer or admin)")]
    [Category("Review")]
    Result<PluginReleaseSummary[]> ListAllPluginReleases(ListAllPluginReleasesRequest request);

    [Summary("Query audit entries (admin only)")]
    [Category("Audit")]
    Result<AuditEntrySummary[]> ListAuditEntries(ListAuditEntriesRequest request);

    [Summary("Get an audit entry by id (admin only)")]
    [Category("Audit")]
    Result<AuditEntrySummary> GetAuditEntry(GetAuditEntryRequest request);
}
