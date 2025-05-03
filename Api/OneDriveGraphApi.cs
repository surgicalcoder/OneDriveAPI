using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using KoenZomers.OneDrive.Api.Entities;
using KoenZomers.OneDrive.Api.Helpers;
using System.Collections.Generic;
using System.Net.Http;
using KoenZomers.OneDrive.Api.Enums;
using System.IO;
using System.Threading;

namespace KoenZomers.OneDrive.Api
{
    /// <summary>
    /// API for both OneDrive Personal and OneDrive for Business on Office 365 through the Microsoft Graph API
    /// Create your own Client ID / Client Secret at https://apps.dev.microsoft.com
    /// </summary>
    public class OneDriveGraphApi : OneDriveApi
    {
        #region Constants

        /// <summary>
        /// The url to provide as the redirect URL after successful authentication
        /// </summary>
        public override string AuthenticationRedirectUrl { get; set; } = "https://login.microsoftonline.com/common/oauth2/nativeclient";

        /// <summary>
        /// String formatted Uri that needs to be called to authenticate to the Graph API
        /// </summary>
        protected override string AuthenticateUri => "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={0}&response_type=code&redirect_uri={1}&response_mode=query&scope=offline_access%20files.readwrite.all";

        /// <summary>
        /// String formatted Uri that can be called to sign out from the Graph API
        /// </summary>
        public override string SignoutUri => "https://login.microsoftonline.com/common/oauth2/v2.0/logout";

        /// <summary>
        /// The url where an access token can be obtained
        /// </summary>
        protected override string AccessTokenUri => "https://login.microsoftonline.com/common/oauth2/v2.0/token";

        /// <summary>
        /// Defines the maximum allowed file size that can be used for basic uploads. Should be set 4 MB as described in the API documentation at https://developer.microsoft.com/en-us/graph/docs/api-reference/v1.0/api/item_uploadcontent
        /// </summary>
        public new static long MaximumBasicFileUploadSizeInBytes = 4 * 1024;

        /// <summary>
        /// Size of the chunks to upload when using the resumable upload method. Must be a multiple of 327680 bytes. See https://developer.microsoft.com/en-us/graph/docs/api-reference/v1.0/api/driveitem_createuploadsession#best-practices
        /// </summary>
        public new long ResumableUploadChunkSizeInBytes = 10485760;

        /// <summary>
        /// The default scopes to request access to at the Graph API
        /// </summary>
        public string[] DefaultScopes => new[] { "offline_access", "files.readwrite.all" };

        /// <summary>
        /// Base URL of the Graph API
        /// </summary>
        protected string GraphApiBaseUrl => "https://graph.microsoft.com/v1.0/";

        #endregion

        #region Constructors

        /// <summary>
        /// Instantiates a new instance of the Graph API
        /// </summary>
        /// <param name="applicationId">Microsoft Application ID to use to connect</param>
        /// <param name="clientSecret">Microsoft Application secret to use to connect</param>
        public OneDriveGraphApi(string applicationId, string clientSecret = null) : base(applicationId, clientSecret)
        {
            OneDriveApiBaseUrl = GraphApiBaseUrl + "me/";
        }

        #endregion

        #region Public Methods - Authentication

        /// <summary>
        /// Returns the Uri that needs to be called to authenticate to the OneDrive for Business API
        /// </summary>
        /// <returns>Uri that needs to be called in a browser to authenticate to the OneDrive for Business API</returns>
        public override Uri GetAuthenticationUri()
        {
            var uri = string.Format(AuthenticateUri, ClientId, AuthenticationRedirectUrl);
            return new Uri(uri);
        }

        /// <summary>
        /// Gets an access token from the provided authorization token using the default scopes defined in DefaultScopes
        /// </summary>
        /// <param name="authorizationToken">Authorization token</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Access token for the Graph API</returns>
        /// <exception cref="Exceptions.TokenRetrievalFailedException">Thrown when unable to retrieve a valid access token</exception>
        protected override async Task<OneDriveAccessToken> GetAccessTokenFromAuthorizationToken(string authorizationToken, CancellationToken cancellationToken = new())
        {
            return await GetAccessTokenFromAuthorizationToken(authorizationToken, DefaultScopes, cancellationToken);
        }

        /// <summary>
        /// Gets an access token from the provided authorization token
        /// </summary>
        /// <param name="authorizationToken">Authorization token</param>
        /// <param name="scopes">Scopes to request access for</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Access token for the Graph API</returns>
        /// <exception cref="Exceptions.TokenRetrievalFailedException">Thrown when unable to retrieve a valid access token</exception>
        protected async Task<OneDriveAccessToken> GetAccessTokenFromAuthorizationToken(string authorizationToken, string[] scopes, CancellationToken cancellationToken = new())
        {
            var queryBuilder = new QueryStringBuilder();
            queryBuilder.Add("client_id", ClientId);
            queryBuilder.Add("scope", scopes.Aggregate((x, y) => $"{x} {y}"));
            queryBuilder.Add("code", authorizationToken);
            queryBuilder.Add("redirect_uri", AuthenticationRedirectUrl);
            queryBuilder.Add("grant_type", "authorization_code");
            if (ClientSecret != null)
                queryBuilder.Add("client_secret", ClientSecret);
            return await PostToTokenEndPoint(queryBuilder, cancellationToken);
        }

        /// <summary>
        /// Gets an access token from the provided refresh token using the default scopes defined in DefaultScopes
        /// </summary>
        /// <param name="refreshToken">Refresh token</param>
        /// <returns>Access token for the Graph API</returns>
        /// <exception cref="Exceptions.TokenRetrievalFailedException">Thrown when unable to retrieve a valid access token</exception>
        protected override async Task<OneDriveAccessToken> GetAccessTokenFromRefreshToken(string refreshToken, CancellationToken cancellationToken = new())
        {
            return await GetAccessTokenFromRefreshToken(refreshToken, DefaultScopes, cancellationToken);
        }

        /// <summary>
        /// Gets an access token from the provided refresh token
        /// </summary>
        /// <param name="refreshToken">Refresh token</param>
        /// <param name="scopes">Scopes to request access for</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Access token for the Graph API</returns>
        /// <exception cref="Exceptions.TokenRetrievalFailedException">Thrown when unable to retrieve a valid access token</exception>
        protected async Task<OneDriveAccessToken> GetAccessTokenFromRefreshToken(string refreshToken, string[] scopes, CancellationToken cancellationToken = new())
        {
            var queryBuilder = new QueryStringBuilder();
            queryBuilder.Add("client_id", ClientId);
            queryBuilder.Add("scope", scopes.Aggregate((x, y) => $"{x} {y}"));
            queryBuilder.Add("refresh_token", refreshToken);
            queryBuilder.Add("redirect_uri", AuthenticationRedirectUrl);
            queryBuilder.Add("grant_type", "refresh_token");
            if (ClientSecret != null)
                queryBuilder.Add("client_secret", ClientSecret);
            return await PostToTokenEndPoint(queryBuilder, cancellationToken);
        }

        #endregion

        #region Public Methods - Validate

        /// <summary>
        /// Validates if the provided filename is valid to be used on OneDrive
        /// </summary>
        /// <param name="filename">Filename to validate</param>
        /// <returns>True if filename is valid to be used, false if it isn't</returns>
        public override bool ValidFilename(string filename)
        {
            char[] restrictedCharacters = { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '#', '%' };
            return filename.IndexOfAny(restrictedCharacters) == -1;
        }

        #endregion

        #region Public Methods - Graph API Only

        #region Sharing

        /// <summary>
        /// Shares a OneDrive item by creating an anonymous link to the item
        /// </summary>
        /// <param name="itemPath">The path to the OneDrive item to share</param>
        /// <param name="linkType">Type of sharing to request</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrivePermission entity representing the share or NULL if the operation fails</returns>
        public override async Task<OneDrivePermission> ShareItem(string itemPath, OneDriveLinkType linkType, CancellationToken cancellationToken = new())
        {
            return await ShareItemInternal($"drive/root:/{itemPath}:/createLink", linkType, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Shares a OneDrive item by creating an anonymous link to the item
        /// </summary>
        /// <param name="item">The OneDrive item to share</param>
        /// <param name="linkType">Type of sharing to request</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrivePermission entity representing the share or NULL if the operation fails</returns>
        public override async Task<OneDrivePermission> ShareItem(OneDriveItem item, OneDriveLinkType linkType, CancellationToken cancellationToken = new())
        {
            return await ShareItemInternal($"drive/items/{item.Id}/createLink", linkType, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Shares a OneDrive item by creating an anonymous link to the item
        /// </summary>
        /// <param name="itemPath">The path to the OneDrive item to share</param>
        /// <param name="linkType">Type of sharing to request</param>
        /// <param name="scope">Scope defining who has access to the shared item</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrivePermission entity representing the share or NULL if the operation fails</returns>
        public async Task<OneDrivePermission> ShareItem(string itemPath, OneDriveLinkType linkType, OneDriveSharingScope scope, CancellationToken cancellationToken = new())
        {
            return await ShareItemInternal($"drive/root:/{itemPath}:/createLink", linkType, scope, cancellationToken);
        }

        /// <summary>
        /// Shares a OneDrive item by creating an anonymous link to the item
        /// </summary>
        /// <param name="item">The OneDrive item to share</param>
        /// <param name="linkType">Type of sharing to request</param>
        /// <param name="scope">Scope defining who has access to the shared item</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrivePermission entity representing the share or NULL if the operation fails</returns>
        public async Task<OneDrivePermission> ShareItem(OneDriveItem item, OneDriveLinkType linkType, OneDriveSharingScope scope, CancellationToken cancellationToken = new())
        {
            return await ShareItemInternal($"drive/items/{item.Id}/createLink", linkType, scope, cancellationToken);
        }

        /// <summary>
        /// Returns all the items that have been shared by others with the current user
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with items that have been shared by others with the current user</returns>
        public override async Task<OneDriveItemCollection> GetSharedWithMe(CancellationToken cancellationToken = new())
        {
            var oneDriveItems = await GetData<OneDriveItemCollection>("drive/sharedWithMe", cancellationToken);
            return oneDriveItems;
        }

        #endregion

        #region Adding permissions

        /// <summary>
        /// Adds permissions to a OneDrive item
        /// </summary>        
        /// <param name="item">The OneDrive item to add the permission to</param>
        /// <param name="permissionRequest">Details of the request for permission</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with OneDrivePermissionResponse objects representing the granted permissions</returns>
        public async Task<OneDriveCollectionResponse<OneDrivePermissionResponse>> AddPermission(OneDriveItem item, OneDrivePermissionRequest permissionRequest, CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/items/{item.Id}/invite";

            var result = await SendMessageReturnOneDriveItem<OneDriveCollectionResponse<OneDrivePermissionResponse>>(permissionRequest, HttpMethod.Post, completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Adds permissions to a OneDrive item
        /// </summary>        
        /// <param name="itemPath">The path to the OneDrive item to add the permission to</param>
        /// <param name="permissionRequest">Details of the request for permission</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with OneDrivePermissionResponse objects representing the granted permissions</returns>
        public async Task<OneDriveCollectionResponse<OneDrivePermissionResponse>> AddPermission(string itemPath, OneDrivePermissionRequest permissionRequest, CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/root:/{itemPath}:/invite";

            var result = await SendMessageReturnOneDriveItem<OneDriveCollectionResponse<OneDrivePermissionResponse>>(permissionRequest, HttpMethod.Post, completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Adds permissions to a OneDrive item
        /// </summary>
        /// <param name="item">The OneDrive item to add the permission to</param>
        /// <param name="requireSignin">Boolean to indicate if the user has to sign in before being able to access the OneDrive item</param>
        /// <param name="linkType">Indicates what type of access should be assigned to the invitees</param>
        /// <param name="emailAddresses">Array with e-mail addresses to receive access to the OneDrive item</param>
        /// <param name="sendInvitation">Send an e-mail to the invitees to inform them about having received permissions to the OneDrive item</param>
        /// <param name="sharingMessage">Custom message to add to the e-mail sent out to the invitees</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with OneDrivePermissionResponse objects representing the granted permissions</returns>
        public async Task<OneDriveCollectionResponse<OneDrivePermissionResponse>> AddPermission(OneDriveItem item, bool requireSignin, bool sendInvitation, OneDriveLinkType linkType, string sharingMessage, string[] emailAddresses, CancellationToken cancellationToken = new())
        {
            var permissionRequest = new OneDrivePermissionRequest
            {
                Message = sharingMessage,
                RequireSignin = requireSignin,
                SendInvitation = sendInvitation,
                Roles = linkType == OneDriveLinkType.View ? ["read"] : ["write"]
            };

            permissionRequest.Recipients = emailAddresses.Select(emailAddress => new OneDriveDriveRecipient { Email = emailAddress }).ToArray();

            return await AddPermission(item, permissionRequest, cancellationToken);
        }

        /// <summary>
        /// Adds permissions to a OneDrive item
        /// </summary>
        /// <param name="itemPath">The path to the OneDrive item to add the permission to</param>
        /// <param name="requireSignin">Boolean to indicate if the user has to sign in before being able to access the OneDrive item</param>
        /// <param name="linkType">Indicates what type of access should be assigned to the invitees</param>
        /// <param name="emailAddresses">Array with e-mail addresses to receive access to the OneDrive item</param>
        /// <param name="sendInvitation">Send an e-mail to the invitees to inform them about having received permissions to the OneDrive item</param>
        /// <param name="sharingMessage">Custom message to add to the e-mail sent out to the invitees</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with OneDrivePermissionResponse objects representing the granted permissions</returns>
        public async Task<OneDriveCollectionResponse<OneDrivePermissionResponse>> AddPermission(string itemPath, bool requireSignin, bool sendInvitation, OneDriveLinkType linkType, string sharingMessage, string[] emailAddresses, CancellationToken cancellationToken = new())
        {
            var permissionRequest = new OneDrivePermissionRequest
            {
                Message = sharingMessage,
                RequireSignin = requireSignin,
                SendInvitation = sendInvitation,
                Roles = linkType == OneDriveLinkType.View ? ["read"] : ["write"]
            };

            permissionRequest.Recipients = emailAddresses.Select(emailAddress => new OneDriveDriveRecipient { Email = emailAddress }).ToArray();

            return await AddPermission(itemPath, permissionRequest, cancellationToken);
        }

        #endregion

        #region Updating permissions

        /// <summary>
        /// Changes permissions on a OneDrive item
        /// </summary>
        /// <param name="item">The OneDrive item to change the permission of</param>
        /// <param name="permissionType">Permission to set on the OneDrive item</param>
        /// <param name="permissionId">ID of the permission object applied to the OneDrive item which needs its permissions changed</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrivePermissionResponse object representing the granted permission</returns>
        public async Task<OneDrivePermissionResponse> ChangePermission(OneDriveItem item, string permissionId, OneDriveLinkType permissionType, CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/items/{item.Id}/permissions/{permissionId}";

            var result = await SendMessageReturnOneDriveItem<OneDrivePermissionResponse>("{ \"roles\": [ \"" + (permissionType == OneDriveLinkType.Edit ? "write" : "read") + "\" ] }", new HttpMethod("PATCH"), completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Changes permissions on a OneDrive item
        /// </summary>
        /// <param name="item">The OneDrive item to change the permission of</param>
        /// <param name="permissionType">Permission to set on the OneDrive item</param>
        /// <param name="permission">Permission object applied to the OneDrive item which needs its permissions changed</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrivePermissionResponse object representing the granted permission</returns>
        public async Task<OneDrivePermissionResponse> ChangePermission(OneDriveItem item, OneDrivePermission permission, OneDriveLinkType permissionType, CancellationToken cancellationToken = new())
        {
            return await ChangePermission(item, permission.Id, permissionType, cancellationToken);
        }

        /// <summary>
        /// Changes permissions on a OneDrive item
        /// </summary>
        /// <param name="itemPath">The path to the OneDrive item to change the permission of</param>
        /// <param name="permissionType">Permission to set on the OneDrive item</param>
        /// <param name="permissionId">ID of the permission object applied to the OneDrive item which needs its permissions changed</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrivePermissionResponse object representing the granted permission</returns>
        public async Task<OneDrivePermissionResponse> ChangePermission(string itemPath, string permissionId, OneDriveLinkType permissionType, CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/root:/{itemPath}:/permissions/{permissionId}";

            // TODO - replace below with anonymous object
            var result = await SendMessageReturnOneDriveItem<OneDrivePermissionResponse>($"{{ \"roles\": [ \"{(permissionType == OneDriveLinkType.Edit ? "write" : "read")}\" ] }}", new HttpMethod("PATCH"), completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Changes permissions on a OneDrive item
        /// </summary>
        /// <param name="itemPath">The path to the OneDrive item to change the permission of</param>
        /// <param name="permissionType">Permission to set on the OneDrive item</param>
        /// <param name="permission">Permission object applied to the OneDrive item which needs its permissions changed</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrivePermissionResponse object representing the granted permission</returns>
        public async Task<OneDrivePermissionResponse> ChangePermission(string itemPath, OneDrivePermission permission, OneDriveLinkType permissionType, CancellationToken cancellationToken = new())
        {
            return await ChangePermission(itemPath, permission.Id, permissionType, cancellationToken);
        }

        #endregion

        #region Removing permissions

        /// <summary>
        /// Removes the permission from a OneDrive item
        /// </summary>
        /// <param name="itemPath">The path to the OneDrive item to remove the permission from</param>
        /// <param name="permissionId">Unique permission identifier as received when addign the permission to the item</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Boolean indicating if the operation was successful (true) or failed (false)</returns>
        public async Task<bool> RemovePermission(string itemPath, string permissionId, CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/root:/{itemPath}:/permissions/{permissionId}";

            var result = await SendMessageReturnBool(null, HttpMethod.Delete, completeUrl, HttpStatusCode.NoContent, cancellationToken: cancellationToken);
            return result;
        }

        /// <summary>
        /// Removes the permission from a OneDrive item
        /// </summary>
        /// <param name="itemPath">The path to the OneDrive item to remove the permission from</param>
        /// <param name="permission">Permission object as received when creating a permission on the item</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Boolean indicating if the operation was successful (true) or failed (false)</returns>
        public async Task<bool> RemovePermission(string itemPath, OneDrivePermission permission, CancellationToken cancellationToken = new())
        {
            return await RemovePermission(itemPath, permission.Id, cancellationToken);
        }

        /// <summary>
        /// Removes the permission from a OneDrive item
        /// </summary>
        /// <param name="item">The OneDrive item to add a permission to</param>
        /// <param name="permissionId">Unique sharing permission identifier as received when adding the permission to the item</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Boolean indicating if the operation was successful (true) or failed (false)</returns>
        public async Task<bool> RemovePermission(OneDriveItem item, string permissionId, CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/items/{item.Id}/permissions/{permissionId}";

            var result = await SendMessageReturnBool(null, HttpMethod.Delete, completeUrl, HttpStatusCode.NoContent, cancellationToken: cancellationToken);
            return result;            
        }

        /// <summary>
        /// Removes the permission from a OneDrive item
        /// </summary>
        /// <param name="item">The OneDrive item to add a permission to</param>
        /// <param name="permission">Permission object as received when creating a permission on the item</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Boolean indicating if the operation was successful (true) or failed (false)</returns>
        public async Task<bool> RemovePermission(OneDriveItem item, OneDrivePermission permission, CancellationToken cancellationToken = new())
        {
            return await RemovePermission(item, permission.Id, cancellationToken);
        }

        #endregion

        #region Listing permissions

        /// <summary>
        /// Lists all permissions on a OneDrive item
        /// </summary>
        /// <param name="itemPath">The path to the OneDrive item to retrieve the permissions of</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with OneDrivePermission objects which indicate the permissions on the item</returns>
        public async Task<OneDriveCollectionResponse<OneDrivePermission>> ListPermissions(string itemPath, CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/root:/{itemPath}:/permissions";

            var result = await SendMessageReturnOneDriveItem<OneDriveCollectionResponse<OneDrivePermission>>(string.Empty, HttpMethod.Get, completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Lists all permissions on a OneDrive item
        /// </summary>
        /// <param name="item">The OneDrive item to retrieve the permissions of</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with OneDrivePermission objects which indicate the permissions on the item</returns>
        public async Task<OneDriveCollectionResponse<OneDrivePermission>> ListPermissions(OneDriveItem item, CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/items/{item.Id}/permissions";

            var result = await SendMessageReturnOneDriveItem<OneDriveCollectionResponse<OneDrivePermission>>(item, HttpMethod.Get, completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        #endregion

        #region AppFolder commands

        #region Retrieving data from the AppFolder

        /// <summary>
        /// Gets the AppFolder root its metadata
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem object with the information about the current App Registration its AppFolder</returns>
        public async Task<OneDriveItem> GetAppFolderMetadata(CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/special/approot";

            var result = await SendMessageReturnOneDriveItem<OneDriveItem>(string.Empty, HttpMethod.Get, completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Gets the first batch of the files in the AppFolder
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with OneDriveItem objects one for each file in the the current App Registration its AppFolder</returns>
        public async Task<OneDriveItemCollection> GetAppFolderChildren(CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/special/approot/children";

            var result = await SendMessageReturnOneDriveItem<OneDriveItemCollection>(string.Empty, HttpMethod.Get, completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Gets all files in the AppFolder
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>Collection with OneDriveItem objects one for each file in the current App Registration its AppFolder</returns>
        public async Task<OneDriveItem[]> GetAllAppFolderChildren(CancellationToken cancellationToken = new())
        {
            var completeUrl = $"{OneDriveApiBaseUrl}drive/special/approot/children";

            var result = await GetAllChildrenInternal(completeUrl, cancellationToken);
            return result;
        }

        /// <summary>
        /// Creates a folder in the AppFolder
        /// </summary>
        /// <param name="folderName">Name of the folder to create within the AppFolder</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem object representing the newly created folder inside the AppFolder</returns>
        public async Task<OneDriveItem> CreateAppFolderFolder(string folderName, CancellationToken cancellationToken = new())
        {
            var result = await CreateFolderInternal("drive/special/approot/children", folderName, cancellationToken);
            return result;
        }

        /// <summary>
        /// Gets the folder with the provided name from the AppFolder or creates it if it doesn't exist yet
        /// </summary>
        /// <param name="folderName">Name of the folder to retrieve or create within the AppFolder</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem object representing the requested folder inside the AppFolder</returns>
        public async Task<OneDriveItem> GetAppFolderFolderOrCreate(string folderName, CancellationToken cancellationToken = new())
        {
            // Try to get the folder
            var folder = await GetData<OneDriveItem>($"drive/special/approot/children/{folderName}", cancellationToken);

            if (folder != null)
            {
                // Folder found, return it
                return folder;
            }

            // Folder not found, create it
            return await CreateAppFolderFolder(folderName, cancellationToken);
        }

        #endregion

        #region Uploading files to AppFolder

        /// <summary>
        /// Uploads the provided file to the AppFolder on OneDrive keeping the original filename
        /// </summary>
        /// <param name="filePath">Full path to the file to upload</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFileToAppFolder(string filePath, CancellationToken cancellationToken = new())
        {
            return await UploadFileToAppFolder(filePath, NameConflictBehavior.Replace, cancellationToken);
        }

        /// <summary>
        /// Uploads the provided file to the AppFolder on OneDrive keeping the original filename
        /// </summary>
        /// <param name="filePath">Full path to the file to upload</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFileToAppFolder(string filePath, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            return await UploadFileToAppFolderAs(filePath, null, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Uploads the provided file to the AppFolder on OneDrive using the provided filename
        /// </summary>
        /// <param name="filePath">Full path to the file to upload</param>
        /// <param name="fileName">Filename to assign to the file on OneDrive</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFileToAppFolderAs(string filePath, string fileName, CancellationToken cancellationToken = new())
        {
            return await UploadFileToAppFolderAs(filePath, fileName, NameConflictBehavior.Replace, cancellationToken);
        }

        /// <summary>
        /// Uploads the provided file to the AppFolder on OneDrive using the provided filename
        /// </summary>
        /// <param name="filePath">Full path to the file to upload</param>
        /// <param name="fileName">Filename to assign to the file on OneDrive</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFileToAppFolderAs(string filePath, string fileName, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            if (!File.Exists(filePath))
            {
                throw new ArgumentException("Provided file could not be found", nameof(filePath));
            }

            // Get a reference to the file to upload
            var fileToUpload = new FileInfo(filePath);

            // If no filename has been provided, use the same filename as the original file has
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = fileToUpload.Name;
            }

            // Verify if the filename does not contain any for OneDrive illegal characters
            if (!ValidFilename(fileName))
            {
                throw new ArgumentException("Provided file contains illegal characters in its filename", nameof(filePath));
            }

            // Use the resumable upload method
            return await UploadFileToAppFolderViaResumableUpload(fileToUpload, fileName, null);
        }

        /// <summary>
        /// Uploads the provided file to the AppFolder on OneDrive using the provided filename
        /// </summary>
        /// <param name="fileStream">Stream to the file to upload</param>
        /// <param name="fileName">Filename to assign to the file on OneDrive</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public virtual async Task<OneDriveItem> UploadFileToAppFolderAs(Stream fileStream, string fileName, CancellationToken cancellationToken = new())
        {
            if (fileStream == null || fileStream == Stream.Null)
            {
                throw new ArgumentNullException(nameof(fileStream));
            }
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            // Verify if the filename does not contain any for OneDrive illegal characters
            if (!ValidFilename(fileName))
            {
                throw new ArgumentException("Provided file contains illegal characters in its filename", nameof(fileName));
            }

            // Verify which upload method should be used
            if (fileStream.Length <= MaximumBasicFileUploadSizeInBytes)
            {
                // Use the basic upload method                
                return await UploadFileToAppFolderViaSimpleUpload(fileStream, fileName, cancellationToken);
            }

            // Use the resumable upload method
            return await UploadFileToAppFolderViaResumableUpload(fileStream, fileName, null, cancellationToken);
        }

        /// <summary>
        /// Performs a file upload to the AppFolder on OneDrive using the simple OneDrive API. Best for small files on reliable network connections.
        /// </summary>
        /// <param name="file">File reference to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>The resulting OneDrive item representing the uploaded file</returns>
        public async Task<OneDriveItem> UploadFileToAppFolderViaSimpleUpload(FileInfo file, string fileName, CancellationToken cancellationToken = new())
        {
            // Read the file to upload
            using (var fileStream = file.OpenRead())
            {
                return await UploadFileToAppFolderViaSimpleUpload(fileStream, fileName, cancellationToken);
            }
        }

        /// <summary>
        /// Performs a file upload to the AppFolder on OneDrive using the simple OneDrive API. Best for small files on reliable network connections.
        /// </summary>
        /// <param name="fileStream">Stream to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>The resulting OneDrive item representing the uploaded file</returns>
        public async Task<OneDriveItem> UploadFileToAppFolderViaSimpleUpload(Stream fileStream, string fileName, CancellationToken cancellationToken = new())
        {
            // Construct the complete URL to call
            var oneDriveUrl = $"{OneDriveApiBaseUrl}drive/special/approot:/{fileName}:/content";

            return await UploadFileViaSimpleUploadInternal(fileStream, oneDriveUrl, cancellationToken);
        }

        /// <summary>
        /// Initiates a resumable upload session to the AppFolder on OneDrive. It doesn't perform the actual upload yet.
        /// </summary>
        /// <param name="fileName">Filename to store the uploaded content under</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveUploadSession instance containing the details where to upload the content to</returns>
        protected async Task<OneDriveUploadSession> CreateResumableUploadSessionForAppFolder(string fileName, NameConflictBehavior nameConflictBehavior = NameConflictBehavior.Replace, CancellationToken cancellationToken = new())
        {
            // Construct the complete URL to call
            var completeUrl = $"{OneDriveApiBaseUrl}drive/special/approot:/{fileName}:/createUploadSession";
            return await CreateResumableUploadSessionInternal(completeUrl, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Uploads a file to the AppFolder on OneDrive using the resumable file upload method
        /// </summary>
        /// <param name="file">FileInfo instance pointing to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="fragmentSizeInBytes">Size in bytes of the fragments to use for uploading. Higher numbers are faster but require more stable connections, lower numbers are slower but work better with unstable connections</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem instance representing the uploaded item</returns>
        public async Task<OneDriveItem> UploadFileToAppFolderViaResumableUpload(FileInfo file, string fileName, long? fragmentSizeInBytes, CancellationToken cancellationToken = new())
        {
            return await UploadFileToAppFolderViaResumableUpload(file, fileName, fragmentSizeInBytes, NameConflictBehavior.Replace, cancellationToken);
        }

        /// <summary>
        /// Uploads a file to the AppFolder on OneDrive using the resumable file upload method
        /// </summary>
        /// <param name="file">FileInfo instance pointing to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="fragmentSizeInBytes">Size in bytes of the fragments to use for uploading. Higher numbers are faster but require more stable connections, lower numbers are slower but work better with unstable connections</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem instance representing the uploaded item</returns>
        public async Task<OneDriveItem> UploadFileToAppFolderViaResumableUpload(FileInfo file, string fileName, long? fragmentSizeInBytes, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            // Open the source file for reading
            using (var fileStream = file.OpenRead())
            {
                return await UploadFileToAppFolderViaResumableUpload(fileStream, fileName, fragmentSizeInBytes, cancellationToken);
            }
        }

        /// <summary>
        /// Uploads a file to the AppFolder on OneDrive using the resumable file upload method
        /// </summary>
        /// <param name="fileStream">Stream pointing to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="fragmentSizeInByte">Size in bytes of the fragments to use for uploading. Higher numbers are faster but require more stable connections, lower numbers are slower but work better with unstable connections.</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem instance representing the uploaded item</returns>
        public async Task<OneDriveItem> UploadFileToAppFolderViaResumableUpload(Stream fileStream, string fileName, long? fragmentSizeInByte, CancellationToken cancellationToken = new())
        {
            return await UploadFileToAppFolderViaResumableUpload(fileStream, fileName, fragmentSizeInByte, NameConflictBehavior.Replace, cancellationToken);
        }

        /// <summary>
        /// Uploads a file to the AppFolder on OneDrive using the resumable file upload method
        /// </summary>
        /// <param name="fileStream">Stream pointing to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="fragmentSizeInByte">Size in bytes of the fragments to use for uploading. Higher numbers are faster but require more stable connections, lower numbers are slower but work better with unstable connections.</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem instance representing the uploaded item</returns>
        public async Task<OneDriveItem> UploadFileToAppFolderViaResumableUpload(Stream fileStream, string fileName, long? fragmentSizeInByte, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            var oneDriveUploadSession = await CreateResumableUploadSessionForAppFolder(fileName, nameConflictBehavior, cancellationToken);
            return await UploadFileViaResumableUploadInternal(fileStream, oneDriveUploadSession, fragmentSizeInByte, cancellationToken);
        }

        #endregion

        #endregion

        /// <summary>
        /// Searches for items on OneDrive with the provided query
        /// </summary>
        /// <param name="query">Search query to use</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>All OneDrive items resulting from the search</returns>
        public override async Task<IList<OneDriveItem>> Search(string query, CancellationToken cancellationToken = new())
        {
            return await base.SearchInternal($"drive/root/search(q='{query}')", cancellationToken);
        }

        /// <summary>
        /// Sends a HTTP POST to OneDrive to copy an item on OneDrive
        /// </summary>
        /// <param name="oneDriveSource">The OneDrive Item to be copied</param>
        /// <param name="oneDriveDestinationParent">The OneDrive parent item to copy the item into</param>
        /// <param name="destinationName">The name of the item at the destination where it will be copied to</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>True if successful, false if failed</returns>
        protected override async Task<bool> CopyItemInternal(OneDriveItem oneDriveSource, OneDriveItem oneDriveDestinationParent, string destinationName, CancellationToken cancellationToken = new())
        {
            // Construct the complete URL to call
            string completeUrl;
            if (oneDriveSource.RemoteItem != null)
            {
                // Item will be copied from another drive
                completeUrl = $"drives/{oneDriveSource.RemoteItem.ParentReference.DriveId}/items/{oneDriveSource.RemoteItem.Id}/copy";
            }
            else if (!string.IsNullOrEmpty(oneDriveSource.ParentReference.DriveId))
            {
                // Item will be coped from another drive
                completeUrl = $"drives/{oneDriveSource.ParentReference.DriveId}/items/{oneDriveSource.Id}/copy";
            }
            else
            {
                // Item will be copied frp, the current user its drive
                completeUrl = $"drive/items/{oneDriveSource.Id}/copy";
            }
            completeUrl = ConstructCompleteUrl(completeUrl);

            // Construct the OneDriveParentItemReference entity with the item to be copied details
            var requestBody = new OneDriveParentItemReference
            {
                ParentReference = new OneDriveItemReference
                {
                    Id = oneDriveDestinationParent.Id
                },
                Name = destinationName
            };

            // Call the OneDrive webservice
            var result = await SendMessageReturnBool(requestBody, HttpMethod.Post, completeUrl, HttpStatusCode.Accepted, true, cancellationToken);
            return result;
        }

        #region File Uploading

        /// <summary>
        /// Uploads the provided file to OneDrive keeping the original filename
        /// </summary>
        /// <param name="filePath">Full path to the file to upload</param>
        /// <param name="oneDriveItem">OneDriveItem of the folder to which the file should be uploaded</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFile(string filePath, OneDriveItem oneDriveItem, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            return await UploadFileAs(filePath, null, oneDriveItem, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Uploads the provided file to OneDrive
        /// </summary>
        /// <param name="filePath">Full path to the file to upload</param>
        /// <param name="oneDriveFolder">Path to a OneDrive folder where to upload the file to</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFile(string filePath, string oneDriveFolder, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            var oneDriveItem = await GetItem(oneDriveFolder);
            return await UploadFile(filePath, oneDriveItem, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Uploads the provided file to OneDrive using the provided filename
        /// </summary>
        /// <param name="fileStream">Stream to the file to upload</param>
        /// <param name="fileName">Filename to assign to the file on OneDrive</param>
        /// <param name="oneDriveFolder">Path to a OneDrive folder where to upload the file to</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFileAs(Stream fileStream, string fileName, string oneDriveFolder, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            var oneDriveItem = await GetItem(oneDriveFolder);
            return await UploadFileAs(fileStream, fileName, oneDriveItem, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Uploads the provided file to OneDrive using the provided filename
        /// </summary>
        /// <param name="filePath">Full path to the file to upload</param>
        /// <param name="fileName">Filename to assign to the file on OneDrive</param>
        /// <param name="oneDriveFolder">Path to a OneDrive folder where to upload the file to</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFileAs(string filePath, string fileName, string oneDriveFolder, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            var oneDriveItem = await GetItem(oneDriveFolder, cancellationToken);
            return await UploadFileAs(filePath, fileName, oneDriveItem, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Uploads the provided file to OneDrive using the provided filename
        /// </summary>
        /// <param name="filePath">Full path to the file to upload</param>
        /// <param name="fileName">Filename to assign to the file on OneDrive</param>
        /// <param name="oneDriveItem">OneDriveItem of the folder to which the file should be uploaded</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFileAs(string filePath, string fileName, OneDriveItem oneDriveItem, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            if (!File.Exists(filePath))
            {
                throw new ArgumentException("Provided file could not be found", nameof(filePath));
            }

            // Get a reference to the file to upload
            var fileToUpload = new FileInfo(filePath);

            // If no filename has been provided, use the same filename as the original file has
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = fileToUpload.Name;
            }

            // Verify if the filename does not contain any for OneDrive illegal characters
            if (!ValidFilename(fileName))
            {
                throw new ArgumentException("Provided file contains illegal characters in its filename", nameof(filePath));
            }

            // Use the resumable upload method since the simple upload method doesn't support naming conflict behavior
            return await UploadFileViaResumableUpload(fileToUpload, fileName, oneDriveItem, null, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Uploads the provided file to OneDrive using the provided filename
        /// </summary>
        /// <param name="fileStream">Stream to the file to upload</param>
        /// <param name="fileName">Filename to assign to the file on OneDrive</param>
        /// <param name="oneDriveItem">OneDriveItem of the folder to which the file should be uploaded</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
        public async Task<OneDriveItem> UploadFileAs(Stream fileStream, string fileName, OneDriveItem oneDriveItem, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            if (fileStream == null || fileStream == Stream.Null)
            {
                throw new ArgumentNullException(nameof(fileStream));
            }
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentNullException(nameof(fileName));
            }
            if (oneDriveItem == null)
            {
                throw new ArgumentNullException(nameof(oneDriveItem));
            }

            // Verify if the filename does not contain any for OneDrive illegal characters
            if (!ValidFilename(fileName))
            {
                throw new ArgumentException("Provided file contains illegal characters in its filename", nameof(fileName));
            }

            // Use the resumable upload method since the simple upload method doesn't support naming conflict behavior
            return await UploadFileViaResumableUpload(fileStream, fileName, oneDriveItem, null, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Uploads a file to OneDrive using the resumable file upload method
        /// </summary>
        /// <param name="fileStream">Stream pointing to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="oneDriveItem">OneDrive item representing the folder to which the file should be uploaded</param>
        /// <param name="fragmentSizeInBytes">Size in bytes of the fragments to use for uploading. Higher numbers are faster but require more stable connections, lower numbers are slower but work better with unstable connections</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem instance representing the uploaded item</returns>
        public async Task<OneDriveItem> UploadFileViaResumableUpload(Stream fileStream, string fileName, OneDriveItem oneDriveItem, long? fragmentSizeInBytes, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            var oneDriveUploadSession = await CreateResumableUploadSession(fileName, oneDriveItem, nameConflictBehavior, cancellationToken);
            return oneDriveUploadSession != null ? await UploadFileViaResumableUploadInternal(fileStream, oneDriveUploadSession, fragmentSizeInBytes, cancellationToken) : null;
        }

        /// <summary>
        /// Uploads a file to OneDrive using the resumable file upload method
        /// </summary>
        /// <param name="file">FileInfo instance pointing to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="oneDriveItem">OneDrive item representing the folder to which the file should be uploaded</param>
        /// <param name="fragmentSizeInBytes">Size in bytes of the fragments to use for uploading. Higher numbers are faster but require more stable connections, lower numbers are slower but work better with unstable connections. Provide NULL to use the default.</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem instance representing the uploaded item</returns>
        public async Task<OneDriveItem> UploadFileViaResumableUpload(FileInfo file, string fileName, OneDriveItem oneDriveItem, long? fragmentSizeInBytes, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            // Open the source file for reading
            using (var fileStream = file.OpenRead())
            {
                return await UploadFileViaResumableUpload(fileStream, fileName, oneDriveItem, fragmentSizeInBytes, nameConflictBehavior, cancellationToken);
            }
        }

        /// <summary>
        /// Uploads a file to OneDrive using the resumable method. Better for large files or unstable network connections.
        /// </summary>
        /// <param name="filePath">Path to the file to upload</param>
        /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
        /// <param name="oneDriveItem">OneDrive item representing the folder to which the file should be uploaded</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem instance representing the uploaded item</returns>
        public async Task<OneDriveItem> UploadFileViaResumableUpload(string filePath, string fileName, OneDriveItem oneDriveItem, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            var file = new FileInfo(filePath);
            return await UploadFileViaResumableUpload(file, fileName, oneDriveItem, null, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Initiates a resumable upload session to OneDrive. It doesn't perform the actual upload yet.
        /// </summary>
        /// <param name="fileName">Filename to store the uploaded content under</param>
        /// <param name="oneDriveItem">OneDriveItem container in which the file should be uploaded</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveUploadSession instance containing the details where to upload the content to</returns>
        protected async Task<OneDriveUploadSession> CreateResumableUploadSession(string fileName, OneDriveItem oneDriveItem, NameConflictBehavior nameConflictBehavior, CancellationToken cancellationToken = new())
        {
            // Construct the complete URL to call
            string completeUrl;
            if (oneDriveItem.RemoteItem != null)
            {
                // Item will be uploaded to another drive
                completeUrl = $"drives/{oneDriveItem.RemoteItem.ParentReference.DriveId}/items/{oneDriveItem.RemoteItem.Id}:/{fileName}:/createUploadSession";
            }
            else if (oneDriveItem.ParentReference != null && !string.IsNullOrEmpty(oneDriveItem.ParentReference.DriveId))
            {
                // Item will be uploaded to another drive
                completeUrl = $"drives/{oneDriveItem.ParentReference.DriveId}/items/{oneDriveItem.Id}:/{fileName}:/createUploadSession";
            }
            else if (!string.IsNullOrEmpty(oneDriveItem.WebUrl) && oneDriveItem.WebUrl.Contains("cid="))
            {
                // Item will be uploaded to another drive. Used by OneDrive Personal when using a shared item.
                completeUrl = $"drives/{oneDriveItem.WebUrl.Remove(0, oneDriveItem.WebUrl.IndexOf("cid=") + 4)}/items/{oneDriveItem.Id}:/{fileName}:/createUploadSession";
            }
            else
            {
                // Item will be uploaded to the current user its drive
                completeUrl = $"drive/items/{oneDriveItem.Id}:/{fileName}:/createUploadSession";
            }

            completeUrl = ConstructCompleteUrl(completeUrl);

            return await CreateResumableUploadSessionInternal(completeUrl, nameConflictBehavior, cancellationToken);
        }

        /// <summary>
        /// Initiates a resumable upload session to OneDrive. It doesn't perform the actual upload yet.
        /// </summary>
        /// <param name="oneDriveUrl">Complete URL to call to create the resumable upload session</param>
        /// <param name="nameConflictBehavior">Defines how to deal with the scenario where a similarly named file already exists at the target location</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveUploadSession instance containing the details where to upload the content to</returns>
        protected async Task<OneDriveUploadSession> CreateResumableUploadSessionInternal(string oneDriveUrl, NameConflictBehavior nameConflictBehavior = NameConflictBehavior.Replace, CancellationToken cancellationToken = new())
        {
            // Construct the OneDriveUploadSessionItemContainer entity with the upload details
            // Add the conflictbehavior header to always overwrite the file if it already exists on OneDrive
            var uploadItemContainer = new OneDriveUploadSessionItemContainer
            {
                Item = new OneDriveUploadSessionItem
                {
                    FilenameConflictBehavior = nameConflictBehavior
                }
            };   
            
            // Call the Graph API webservice
            var result = await SendMessageReturnOneDriveItem<OneDriveUploadSession>(uploadItemContainer, HttpMethod.Post, oneDriveUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Uploads a file to OneDrive using the resumable file upload method
        /// </summary>
        /// <param name="fileStream">Stream with the content to upload</param>
        /// <param name="oneDriveUploadSession">Upload session under which the upload will be performed</param>
        /// <param name="fragmentSizeInBytes">Size in bytes of the fragments to use for uploading. Higher numbers are faster but require more stable connections, lower numbers are slower but work better with unstable connections.</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveItem instance representing the uploaded item</returns>
        protected override async Task<OneDriveItem> UploadFileViaResumableUploadInternal(Stream fileStream, OneDriveUploadSession oneDriveUploadSession, long? fragmentSizeInBytes, CancellationToken cancellationToken = new())
        {
            return await base.UploadFileViaResumableUploadInternal(fileStream, oneDriveUploadSession, fragmentSizeInBytes ?? ResumableUploadChunkSizeInBytes, cancellationToken);
        }

        /// <summary>
        /// Initiates a resumable upload session to OneDrive to overwrite an existing file. It doesn't perform the actual upload yet.
        /// </summary>
        /// <param name="oneDriveItem">OneDriveItem item for which updated content will be uploaded</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveUploadSession instance containing the details where to upload the content to</returns>
        protected override async Task<OneDriveUploadSession> CreateResumableUploadSession(OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
        {
            // Construct the complete URL to call
            string completeUrl;
            if (oneDriveItem.RemoteItem != null)
            {
                // Item will be uploaded to another drive
                completeUrl = $"drives/{oneDriveItem.RemoteItem.ParentReference.DriveId}/items/{oneDriveItem.RemoteItem.Id}/createUploadSession";
            }
            else if (oneDriveItem.ParentReference != null && !string.IsNullOrEmpty(oneDriveItem.ParentReference.DriveId))
            {
                // Item will be uploaded to another drive
                completeUrl = $"drives/{oneDriveItem.ParentReference.DriveId}/items/{oneDriveItem.Id}/createUploadSession";
            }
            else if (!string.IsNullOrEmpty(oneDriveItem.WebUrl) && oneDriveItem.WebUrl.Contains("cid="))
            {
                // Item will be uploaded to another drive. Used by OneDrive Personal when using a shared item.
                completeUrl = $"drives/{oneDriveItem.WebUrl.Remove(0, oneDriveItem.WebUrl.IndexOf("cid=", StringComparison.Ordinal) + 4)}/items/{oneDriveItem.Id}/createUploadSession";
            }
            else
            {
                // Item will be uploaded to the current user its drive
                completeUrl = $"drive/items/{oneDriveItem.Id}/createUploadSession";
            }

            completeUrl = ConstructCompleteUrl(completeUrl);

            // Construct the OneDriveUploadSessionItemContainer entity with the upload details
            // Add the conflictbehavior header to always overwrite the file if it already exists on OneDrive
            var uploadItemContainer = new OneDriveUploadSessionItemContainer
            {
                Item = new OneDriveUploadSessionItem
                {
                    FilenameConflictBehavior = NameConflictBehavior.Replace
                }
            };

            // Call the OneDrive webservice
            var result = await SendMessageReturnOneDriveItem<OneDriveUploadSession>(uploadItemContainer, HttpMethod.Post, completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        /// <summary>
        /// Initiates a resumable upload session to OneDrive. It doesn't perform the actual upload yet.
        /// </summary>
        /// <param name="fileName">Filename to store the uploaded content under</param>
        /// <param name="oneDriveFolder">OneDriveItem container in which the file should be uploaded</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDriveUploadSession instance containing the details where to upload the content to</returns>
        protected override async Task<OneDriveUploadSession> CreateResumableUploadSession(string fileName, OneDriveItem oneDriveFolder, CancellationToken cancellationToken = new())
        {
            // Construct the complete URL to call
            string completeUrl;
            if (oneDriveFolder.RemoteItem != null)
            {
                // Item will be uploaded to another drive
                completeUrl = $"drives/{oneDriveFolder.RemoteItem.ParentReference.DriveId}/items/{oneDriveFolder.RemoteItem.Id}:/{fileName}:/createUploadSession";
            }
            else if (oneDriveFolder.ParentReference != null && !string.IsNullOrEmpty(oneDriveFolder.ParentReference.DriveId))
            {
                // Item will be uploaded to another drive
                completeUrl = $"drives/{oneDriveFolder.ParentReference.DriveId}/items/{oneDriveFolder.Id}:/{fileName}:/createUploadSession";
            }
            else if (!string.IsNullOrEmpty(oneDriveFolder.WebUrl) && oneDriveFolder.WebUrl.Contains("cid="))
            {
                // Item will be uploaded to another drive. Used by OneDrive Personal when using a shared item.
                completeUrl = $"drives/{oneDriveFolder.WebUrl.Remove(0, oneDriveFolder.WebUrl.IndexOf("cid=") + 4)}/items/{oneDriveFolder.Id}:/{fileName}:/createUploadSession";
            }
            else
            {
                // Item will be uploaded to the current user its drive
                completeUrl = $"drive/items/{oneDriveFolder.Id}:/{fileName}:/createUploadSession";
            }

            completeUrl = ConstructCompleteUrl(completeUrl);

            // Construct the OneDriveUploadSessionItemContainer entity with the upload details
            // Add the conflictbehavior header to always overwrite the file if it already exists on OneDrive
            var uploadItemContainer = new OneDriveUploadSessionItemContainer
            {
                Item = new OneDriveUploadSessionItem
                {
                    FilenameConflictBehavior = NameConflictBehavior.Replace
                }
            };

            // Call the OneDrive webservice
            var result = await SendMessageReturnOneDriveItem<OneDriveUploadSession>(uploadItemContainer, HttpMethod.Post, completeUrl, HttpStatusCode.OK, cancellationToken);
            return result;
        }

        #endregion

        /// <summary>
        /// Gets the root SharePoint site
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>SharePointSite instance containing the details of the requested site in SharePoint</returns>
        public virtual async Task<SharePointSite> GetSiteRoot(CancellationToken cancellationToken = new())
        {
            return await GetGraphData<SharePointSite>("sites/root");
        }

        /// <summary>
        /// Gets a SharePoint site by its unique identifier
        /// </summary>
        /// <param name="siteId">Unique identifier of the SharePoint site to retrieve, i.e. tenant.sharepoint.com,42f21fb5-a809-41d6-a97c-64ea0935306f,5a153572-749b-45e8-bae3-4a5e108ffa85</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>SharePointSite instance containing the details of the requested site in SharePoint</returns>
        public virtual async Task<SharePointSite> GetSiteById(string siteId, CancellationToken cancellationToken = new())
        {
            return await GetGraphData<SharePointSite>($"sites/{siteId}", cancellationToken);
        }

        /// <summary>
        /// Gets a SharePoint site by its hostname and path
        /// </summary>
        /// <param name="hostname">Full SharePoint Online domain to request the SharePoint site from, i.e. tenant.sharepoint.com</param>
        /// <param name="sitePath">SharePoint tenant relative URL of the site to retrieve, i.e. /sites/team1</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>SharePointSite instance containing the details of the requested site in SharePoint</returns>
        public virtual async Task<SharePointSite> GetSiteByPath(string hostname, string sitePath, CancellationToken cancellationToken = new())
        {
            return await GetGraphData<SharePointSite>($"sites/{hostname}:/{sitePath}", cancellationToken);
        }

        /// <summary>
        /// Gets a SharePoint site belonging to a group
        /// </summary>
        /// <param name="groupId">Unique identifier of group to retrieve the associated SharePoint site for</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>SharePointSite instance containing the details of the requested site in SharePoint</returns>
        public virtual async Task<SharePointSite> GetSiteByGroupId(string groupId, CancellationToken cancellationToken = new())
        {
            return await GetGraphData<SharePointSite>($"groups/{groupId}/sites/root", cancellationToken);
        }

        /// <summary>
        /// Retrieves data from the Graph API
        /// </summary>
        /// <typeparam name="T">Type of OneDrive entity to expect to be returned</typeparam>
        /// <param name="url">Url fragment after the Graph base Uri which indicated the type of information to return</param>
        /// <param name="cancellationToken">Cancellation Token (optional)</param>
        /// <returns>OneDrive entity filled with the information retrieved from the Graph API</returns>
        protected virtual async Task<T> GetGraphData<T>(string url, CancellationToken cancellationToken = new()) where T : OneDriveItemBase
        {
            // Construct the complete URL to call
            var completeUrl = url.StartsWith("http", StringComparison.InvariantCultureIgnoreCase) ? url : string.Concat(GraphApiBaseUrl, url);

            return await base.GetData<T>(completeUrl, cancellationToken);
        }

        /// <summary>
        /// Constructs the complete Url to be called based on the part of the url provided that contains the command
        /// </summary>
        /// <param name="commandUrl">Part of the URL to call that contains the command to execute for the API that is being called</param>
        /// <returns>Full URL to call the API</returns>
        protected override string ConstructCompleteUrl(string commandUrl)
        {
            if(commandUrl.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
            {
                return commandUrl;
            }
            return string.Concat(commandUrl.StartsWith("drives/", StringComparison.InvariantCultureIgnoreCase) ? GraphApiBaseUrl : OneDriveApiBaseUrl, commandUrl);
        }

        #endregion
    }
}
