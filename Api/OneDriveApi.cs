﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using GoLive.OneDrive.Api.Entities;
using GoLive.OneDrive.Api.Enums;
using GoLive.OneDrive.Api.Exceptions;
using GoLive.OneDrive.Api.Helpers;

namespace GoLive.OneDrive.Api;

/// <summary>
/// Base OneDrive API functionality that is valid for either the Consumer OneDrive or the OneDrive for Business
/// platform
/// </summary>
public abstract class OneDriveApi
{
    public static JsonSerializerOptions JSONOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #region Constructors

    /// <summary>
    /// Instantiates a new instance of a OneDrive API
    /// </summary>
    /// <param name="clientId">OneDrive Client ID to use to connect</param>
    /// <param name="clientSecret">OneDrive Client Secret to use to connect</param>
    protected OneDriveApi(string clientId, string clientSecret)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
    }

    #endregion

    #region Events

    /// <summary>
    /// Event triggered when uploading a file using UploadFileViaResumableUpload to indicate the progress of the upload
    /// process
    /// </summary>
    public event EventHandler<OneDriveUploadProgressChangedEventArgs> UploadProgressChanged;

    #endregion

    #region Public Methods - Validate

    /// <summary>
    /// Validates if the provided filename is valid to be used on OneDrive
    /// </summary>
    /// <param name="filename">Filename to validate</param>
    /// <returns>True if filename is valid to be used, false if it isn't</returns>
    public virtual bool ValidFilename(string filename)
    {
        return true;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The oAuth 2.0 Application Client ID
    /// </summary>
    public string ClientId { get; protected set; }

    /// <summary>
    /// The oAuth 2.0 Application Client Secret
    /// </summary>
    public string ClientSecret { get; protected set; }

    /// <summary>
    /// If provided, this proxy will be used for communication with the OneDrive API. If not provided, no proxy will be
    /// used.
    /// </summary>
    public IWebProxy ProxyConfiguration { get; set; }

    /// <summary>
    /// If provided along with a proxy configuration, these credentials will be used to authenticate to the proxy. If
    /// omitted, the default system credentials will be used.
    /// </summary>
    public NetworkCredential ProxyCredential { get; set; }

    /// <summary>
    /// Authorization token used for requesting tokens
    /// </summary>
    public string AuthorizationToken { get; private set; }

    /// <summary>
    /// Access Token for communicating with OneDrive
    /// </summary>
    public OneDriveAccessToken AccessToken { get; protected set; }

    /// <summary>
    /// Date and time until which the access token should be valid based on the information provided by the oAuth provider
    /// </summary>
    public DateTime? AccessTokenValidUntil { get; protected set; }

    /// <summary>
    /// Base URL of the OneDrive API
    /// </summary>
    protected string OneDriveApiBaseUrl { get; set; }

    /// <summary>
    /// Defines the maximum allowed file size that can be used for basic uploads
    /// </summary>
    public static long MaximumBasicFileUploadSizeInBytes = 4 * 1024000;

    /// <summary>
    /// Size of the chunks to upload when using the resumable upload method
    /// </summary>
    public long ResumableUploadChunkSizeInBytes = 5000000;

    #endregion

    #region Abstract Properties

    /// <summary>
    /// The url to provide as the redirect URL after successful authentication
    /// </summary>
    public abstract string AuthenticationRedirectUrl { get; set; }

    /// <summary>
    /// String formatted Uri that needs to be called to authenticate
    /// </summary>
    protected abstract string AuthenticateUri { get; }

    /// <summary>
    /// The url where an access token can be obtained
    /// </summary>
    protected abstract string AccessTokenUri { get; }

    /// <summary>
    /// String formatted Uri that can be called to sign out from the OneDrive API
    /// </summary>
    public abstract string SignoutUri { get; }

    #endregion

    #region Public Methods - Authentication

    /// <summary>
    /// Returns the Uri that needs to be called to authenticate to the OneDrive API
    /// </summary>
    /// <returns>Uri that needs to be called in a browser to authenticate to the OneDrive API</returns>
    public abstract Uri GetAuthenticationUri();

    /// <summary>
    /// Returns the authorization token from the provided URL to which the OneDrive API authentication request was sent
    /// after succesful authentication
    /// </summary>
    /// <param name="url">Url received from the OneDrive API after succesful authentication</param>
    /// <returns>Authorization token or NULL if unable to identify from provided URL</returns>
    public string GetAuthorizationTokenFromUrl(string url)
    {
        // Url must be provided
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        // Url must start with the return url followed by a question mark to provide querystring parameters
        if (!url.StartsWith($"{AuthenticationRedirectUrl}?") && !url.StartsWith($"{AuthenticationRedirectUrl}/?"))
        {
            return null;
        }

        // Get the querystring parameters from the URL
        var queryString = url.Remove(0, AuthenticationRedirectUrl.Length + 1);
        var queryStringParams = HttpUtility.ParseQueryString(queryString);

        AuthorizationToken = queryStringParams["code"];

        return AuthorizationToken;
    }

    /// <summary>
    /// Tries to retrieve an access token based on the tokens already available in this OneDrive instance
    /// </summary>
    /// <returns>OneDrive access token or NULL if unable to get an access token</returns>
    public async Task<OneDriveAccessToken> GetAccessToken(CancellationToken cancellationToken = new())
    {
        // Check if we have an access token
        if (AccessToken != null)
        {
            // We have an access token, check if its still valid
            if (AccessTokenValidUntil.HasValue && AccessTokenValidUntil.Value > DateTime.Now)
            {
                // Access token is still valid, use it
                return AccessToken;
            }

            // Access token is no longer valid, check if we have a refresh token to request a new access token
            if (!string.IsNullOrEmpty(AccessToken.RefreshToken))
            {
                // We have a refresh token, request a new access token using it
                AccessToken = await GetAccessTokenFromRefreshToken(AccessToken.RefreshToken, cancellationToken);

                return AccessToken;
            }
        }

        // No access token is available, check if we have an authorization token
        if (string.IsNullOrEmpty(AuthorizationToken))
        {
            // No access token, no authorization token, we need to authorize first which can't be done without an UI
            return null;
        }

        // No access token but we have an authorization token, request the access token
        AccessToken = await GetAccessTokenFromAuthorizationToken(AuthorizationToken, cancellationToken);
        AccessTokenValidUntil = DateTime.Now.AddSeconds(AccessToken.AccessTokenExpirationDuration);

        return AccessToken;
    }

    /// <summary>
    /// Returns the Uri that needs to be called to sign the current user out of the OneDrive API
    /// </summary>
    /// <returns>Uri that needs to be called to sign the current user out of the OneDrive API</returns>
    public Uri GetSignOutUri()
    {
        return new Uri(string.Format(SignoutUri, ClientId));
    }

    /// <summary>
    /// Sends a HTTP POST to the OneDrive Token EndPoint
    /// </summary>
    /// <param name="queryBuilder">The querystring parameters to send in the POST body</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Access token for OneDrive or NULL if unable to retrieve an access token</returns>
    /// <exception cref="Exceptions.TokenRetrievalFailedException">Thrown when unable to retrieve a valid access token</exception>
    protected async Task<OneDriveAccessToken> PostToTokenEndPoint(QueryStringBuilder queryBuilder, CancellationToken cancellationToken = new())
    {
        if (string.IsNullOrEmpty(AccessTokenUri))
        {
            throw new InvalidOperationException("AccessTokenUri has not been set");
        }

        // Create an HTTPClient instance to communicate with the REST API of OneDrive
        using (var client = CreateHttpClient())
        {
            // Load the content to upload
            using (var content = new StringContent(queryBuilder.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded"))
            {
                // Construct the message towards the webservice
                var options = new JsonSerializerOptions();

                using (var request = new HttpRequestMessage(HttpMethod.Post, AccessTokenUri))
                {
                    // Set the content to send along in the message body with the request
                    request.Content = content;

                    // Request the response from the webservice
                    var response = await client.SendAsync(request, cancellationToken);
                    await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                    options.Converters.Add(new JsonStringEnumConverter());

                    // Verify if the request was successful (response status 200-299)
                    if (response.IsSuccessStatusCode)
                    {
                        // Successfully retrieved token, parse it from the response
                        var appTokenResult = await JsonSerializer.DeserializeAsync<OneDriveAccessToken>(responseStream, options, cancellationToken);

                        return appTokenResult;
                    }

                    // Not able to retrieve a token, parse the error and throw it as an exception
                    OneDriveError errorResult;

                    try
                    {
                        // Try to parse the response as a OneDrive API error message
                        errorResult = await JsonSerializer.DeserializeAsync<OneDriveError>(responseStream, options, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        throw new TokenRetrievalFailedException(innerException: ex);
                    }

                    throw new TokenRetrievalFailedException(errorResult.ErrorDescription, errorResult);
                }
            }
        }
    }

    /// <summary>
    /// Authenticates to OneDrive using the provided Refresh Token
    /// </summary>
    /// <param name="refreshToken">Refreshtoken to use to authenticate to OneDrive</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    public async Task AuthenticateUsingRefreshToken(string refreshToken, CancellationToken cancellationToken = new())
    {
        AccessToken = await GetAccessTokenFromRefreshToken(refreshToken);
        AccessTokenValidUntil = DateTime.Now.AddSeconds(AccessToken.AccessTokenExpirationDuration);
    }

    /// <summary>
    /// Gets an access token from the provided authorization token
    /// </summary>
    /// <param name="authorizationToken">Authorization token</param>
    /// ///
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Access token for OneDrive or NULL if unable to retrieve an access token</returns>
    protected abstract Task<OneDriveAccessToken> GetAccessTokenFromAuthorizationToken(string authorizationToken, CancellationToken cancellationToken = new());

    /// <summary>
    /// Gets an access token from the provided refresh token
    /// </summary>
    /// <param name="refreshToken">Refresh token</param>
    /// ///
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Access token for OneDrive or NULL if unable to retrieve an access token</returns>
    protected abstract Task<OneDriveAccessToken> GetAccessTokenFromRefreshToken(string refreshToken, CancellationToken cancellationToken = new());

    #endregion

    #region Public Methods - Getting content

    /// <summary>
    /// Retrieves the current OneDrive drive information
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<OneDriveDrive> GetDrive(CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveDrive>("drive", cancellationToken);
    }

    /// <summary>
    /// Retrieves the OneDrive drive information from the provided drive
    /// </summary>
    /// <param name="driveId">Id of the drive to retrieve</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<OneDriveDrive> GetDrive(string driveId, CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveDrive>($"drives/{driveId}", cancellationToken);
    }

    /// <summary>
    /// Retrieves the current OneDrive drive information from the provided drive
    /// </summary>
    /// <param name="drive">Drive to retrieve</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<OneDriveDrive> GetDrive(OneDriveDrive drive, CancellationToken cancellationToken = new())
    {
        return await GetDrive(drive.Id, cancellationToken);
    }

    /// <summary>
    /// Retrieves the OneDrive root folder
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<OneDriveItem> GetDriveRoot(CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItem>("drive/root", cancellationToken);
    }

    /// <summary>
    /// Retrieves the first batch of children under the OneDrive root folder
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItemCollection containing the first batch of items in the root folder</returns>
    public virtual async Task<OneDriveItemCollection> GetDriveRootChildren(CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItemCollection>("drive/root/children", cancellationToken);
    }

    /// <summary>
    /// Retrieves all the children under the OneDrive root folder
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem array containing all items in the requested folder</returns>
    public virtual async Task<OneDriveItem[]> GetAllDriveRootChildren(CancellationToken cancellationToken = new())
    {
        return await GetAllChildrenInternal("drive/root/children", cancellationToken);
    }

    /// <summary>
    /// Retrieves the first batch of children under the provided OneDrive path. Use GetNextChildrenByPath and provide the
    /// NextLink from the results to fetch the next batch.
    /// </summary>
    /// <param name="path">Path within OneDrive to retrieve the child items of</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItemCollection containing the first batch of items in the requested folder</returns>
    public virtual async Task<OneDriveItemCollection> GetChildrenByPath(string path, CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItemCollection>($"drive/root:/{path}:/children", cancellationToken);
    }

    /// <summary>
    /// Retrieves a next batch of children using the provided full SkipToken path
    /// </summary>
    /// <param name="skipTokenUrl">Full URL from a NextLink in the response of a GetChildrenByPath request</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItemCollection containing the next batch of items in the requested folder</returns>
    public virtual async Task<OneDriveItemCollection> GetNextChildrenByPath(string skipTokenUrl, CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItemCollection>(skipTokenUrl, cancellationToken);
    }

    /// <summary>
    /// Retrieves all children under the provided OneDrive path
    /// </summary>
    /// <param name="path">Path within OneDrive to retrieve the child items of</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem array containing all items in the requested folder</returns>
    public virtual async Task<OneDriveItem[]> GetAllChildrenByPath(string path, CancellationToken cancellationToken = new())
    {
        return await GetAllChildrenInternal($"drive/root:/{path}:/children", cancellationToken);
    }

    /// <summary>
    /// Retrieves the first batch of children under the OneDrive folder with the provided id
    /// </summary>
    /// <param name="id">Unique identifier of the folder under which to retrieve the child items</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItemCollection containing the first batch of items in the folder</returns>
    public virtual async Task<OneDriveItemCollection> GetChildrenByFolderId(string id, CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItemCollection>($"drive/items/{id}/children", cancellationToken);
    }

    /// <summary>
    /// Retrieves all the children under the OneDrive folder with the provided id
    /// </summary>
    /// <param name="id">Unique identifier of the folder under which to retrieve the child items</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem array containing all items in the requested folder</returns>
    public virtual async Task<OneDriveItem[]> GetAllChildrenByFolderId(string id, CancellationToken cancellationToken = new())
    {
        return await GetAllChildrenInternal($"drive/items/{id}/children", cancellationToken);
    }

    /// <summary>
    /// Retrieves the firsth batch of children under the provided OneDrive Item
    /// </summary>
    /// <param name="item">OneDrive item to retrieve the child items of</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItemCollection containing the first batch of items in the folder</returns>
    public virtual async Task<OneDriveItemCollection> GetChildrenByParentItem(OneDriveItem item, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (item.RemoteItem != null)
        {
            // Item to get the children from is shared from another drive
            completeUrl = $"drives/{item.RemoteItem.ParentReference.DriveId}/items/{item.RemoteItem.Id}/children";
        }
        else if (!string.IsNullOrEmpty(item.ParentReference.DriveId))
        {
            // Item to get the children from is shared from another drive
            completeUrl = $"drives/{item.ParentReference.DriveId}/items/{item.Id}/children";
        }
        else
        {
            // Item to get the children from resides on the current user its drive
            completeUrl = $"drive/items/{item.Id}/children";
        }

        return await GetData<OneDriveItemCollection>(completeUrl, cancellationToken);
    }

    /// <summary>
    /// Retrieves all the children under the OneDrive folder with the provided id
    /// </summary>
    /// <param name="item">OneDrive item to retrieve the child items of</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem array containing all items in the requested folder</returns>
    public virtual async Task<OneDriveItem[]> GetAllChildrenByParentItem(OneDriveItem item, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (item.RemoteItem != null)
        {
            // Item to get the children from is shared from another drive
            completeUrl = $"drives/{item.RemoteItem.ParentReference.DriveId}/items/{item.RemoteItem.Id}/children";
        }
        else if (!string.IsNullOrEmpty(item.ParentReference.DriveId))
        {
            // Item to get the children from is shared from another drive
            completeUrl = $"drives/{item.ParentReference.DriveId}/items/{item.Id}/children";
        }
        else
        {
            // Item to get the children from resides on the current user its drive
            completeUrl = $"drive/items/{item.Id}/children";
        }

        return await GetAllChildrenInternal(completeUrl, cancellationToken);
    }

    /// <summary>
    /// Retrieves all the children under the OneDrive folder with the provided id
    /// </summary>
    /// <param name="fetchUrl">Url to use to fetch the first set of child items</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem array containing all items in the requested folder</returns>
    protected async Task<OneDriveItem[]> GetAllChildrenInternal(string fetchUrl, CancellationToken cancellationToken = new())
    {
        var results = new List<OneDriveItem>();

        do
        {
            // Retrieve a batch with child items
            var resultSet = await GetData<OneDriveItemCollection>(fetchUrl, cancellationToken);

            // Add the batch results to the complete list with results
            results.AddRange(resultSet.Collection);

            // Check if there's a NextLink in the response. If so, continue with fetching the next bach of items. If not, we're done.
            if (string.IsNullOrEmpty(resultSet.NextLink))
            {
                break;
            }

            // Set the url where to get the next batch of items based on the NextLink provided in the previous batch results
            fetchUrl = resultSet.NextLink;
        } while (true);

        return results.ToArray();
    }

    /// <summary>
    /// Retrieves the OneDrive Item by its complete path to the file
    /// </summary>
    /// <param name="path">Path of the OneDrive item to retrieve</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the file or NULL if the file was not found</returns>
    public virtual async Task<OneDriveItem> GetItem(string path, CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItem>($"drive/root:/{path}", cancellationToken);
    }

    /// <summary>
    /// Retrieves the OneDrive Item by it's filename in a specific folder
    /// </summary>
    /// <param name="folder">OneDriveItem representing the folder in which the file should reside</param>
    /// <param name="fileName">File name of the file to retrieve</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the file or NULL if the file was not found</returns>
    public virtual async Task<OneDriveItem> GetItemInFolder(OneDriveItem folder, string fileName, CancellationToken cancellationToken = new())
    {
        var itemsInFolder = await GetAllChildrenByParentItem(folder, cancellationToken);
        var item = itemsInFolder.FirstOrDefault(i => string.Equals(i.Name, fileName, StringComparison.InvariantCultureIgnoreCase));

        return item;
    }

    /// <summary>
    /// Retrieves the OneDrive Item by it's filename in a specific folder
    /// </summary>
    /// <param name="folderId">Unique identifier of the folder in which the file should reside</param>
    /// <param name="fileName">File name of the file to retrieve</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the file or NULL if the file was not found</returns>
    public virtual async Task<OneDriveItem> GetItemInFolder(string folderId, string fileName, CancellationToken cancellationToken = new())
    {
        var folder = await GetItemById(folderId, cancellationToken);

        if (folder == null)
        {
            return null;
        }

        return await GetItemInFolder(folder, fileName, cancellationToken);
    }

    /// <summary>
    /// Retrieves the OneDrive Item from the current drive by it's unique identifier
    /// </summary>
    /// <param name="id">Unique identifier of the OneDrive item to retrieve</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the file or NULL if the file was not found</returns>
    public virtual async Task<OneDriveItem> GetItemById(string id, CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItem>($"drive/items/{id}", cancellationToken);
    }

    /// <summary>
    /// Retrieves the OneDrive Item from the provided drive by it's unique identifier
    /// </summary>
    /// <param name="id">Unique identifier of the OneDrive item to retrieve</param>
    /// <param name="driveId">Id of the drive on which the item resides</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the file or NULL if the file was not found</returns>
    public virtual async Task<OneDriveItem> GetItemFromDriveById(string id, string driveId, CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItem>($"drives/{driveId}/items/{id}", cancellationToken);
    }

    /// <summary>
    /// Retrieves the OneDrive Item from the provided drive by it's unique identifier
    /// </summary>
    /// <param name="id">Unique identifier of the OneDrive item to retrieve</param>
    /// <param name="drive">Drive on which the item resides</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the file or NULL if the file was not found</returns>
    public virtual async Task<OneDriveItem> GetItemFromDriveById(string id, OneDriveDrive drive, CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItem>($"drives/{drive.Id}/items/{id}", cancellationToken);
    }

    /// <summary>
    /// Retrieves the OneDrive folder item or creates it if it doesn't exist yet
    /// </summary>
    /// <param name="path">
    /// Path of the OneDrive folder to retrieve or create. It will ensure that the whole provided path
    /// exists and create (sub)folders if they don't exist yet
    /// </param>
    /// <example>Files\Work\Contracts</example>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the folder designated in the path</returns>
    public virtual async Task<OneDriveItem> GetFolderOrCreate(string path, CancellationToken cancellationToken = new())
    {
        // Replace possible forward slashes with backslashes
        path = path.Replace("/", "\\");

        // Check if the path contains multiple folders
        if (path.Contains("\\"))
        {
            // Path contains multiple folders, use recursion to ensure the entire path exists
            await GetFolderOrCreate(path.Remove(path.LastIndexOf("\\", StringComparison.Ordinal)), cancellationToken);
        }

        // Try to get the folder
        var folder = await GetData<OneDriveItem>($"drive/root:/{path}", cancellationToken);

        if (folder != null)
        {
            // Folder found, return it
            return folder;
        }

        // Folder not found, create it
        var folderName = path.Contains("\\") ? path.Remove(0, path.LastIndexOf("\\", StringComparison.Ordinal) + 1) : path;
        var parentPath = path.Contains("\\") ? path.Remove(path.Length - folderName.Length - 1) : "";
        folder = await CreateFolder(parentPath, folderName, cancellationToken);

        return folder;
    }

    /// <summary>
    /// Retrieves the items in the CameraRoll folder
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<OneDriveItemCollection> GetDriveCameraRollFolder(CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItemCollection>("drive/special/cameraroll", cancellationToken);
    }

    /// <summary>
    /// Retrieves the items in the Documents folder
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<OneDriveItemCollection> GetDriveDocumentsFolder(CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItemCollection>("drive/special/documents", cancellationToken);
    }

    /// <summary>
    /// Retrieves the items in the Photos folder
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<OneDriveItemCollection> GetDrivePhotosFolder(CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItemCollection>("drive/special/photos", cancellationToken);
    }

    /// <summary>
    /// Retrieves the items in the Public folder
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<OneDriveItemCollection> GetDrivePublicFolder(CancellationToken cancellationToken = new())
    {
        return await GetData<OneDriveItemCollection>("drive/special/public", cancellationToken);
    }

    /// <summary>
    /// Searches for items on OneDrive with the provided query
    /// </summary>
    /// <param name="query">Search query to use</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>All OneDrive items resulting from the search</returns>
    public virtual async Task<IList<OneDriveItem>> Search(string query, CancellationToken cancellationToken = new())
    {
        return await SearchInternal($"drive/root/view.search?q={query}", cancellationToken);
    }

    /// <summary>
    /// Searches for items on OneDrive in the provided path with the provided query
    /// </summary>
    /// <param name="query">Search query to use</param>
    /// <param name="path">OneDrive path where to search in</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>All OneDrive items resulting from the search</returns>
    public virtual async Task<IList<OneDriveItem>> Search(string query, string path, CancellationToken cancellationToken = new())
    {
        return await SearchInternal($"drive/root:/{path}/view.search?q={query}", cancellationToken);
    }

    /// <summary>
    /// Searches for items on OneDrive in the provided path with the provided query
    /// </summary>
    /// <param name="query">Search query to use</param>
    /// <param name="oneDriveItem">OneDrive item representing a folder to search in</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>All OneDrive items resulting from the search</returns>
    public virtual async Task<IList<OneDriveItem>> Search(string query, OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        return await SearchInternal($"drive/items/{oneDriveItem.Id}/view.search?q={query}", cancellationToken);
    }

    /// <summary>
    /// Deletes the provided OneDriveItem from OneDrive
    /// </summary>
    /// <param name="oneDriveItem">The OneDriveItem reference to delete from OneDrive</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<bool> Delete(OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveItem.RemoteItem != null)
        {
            // Item to delete is shared from another drive
            completeUrl = $"drives/{oneDriveItem.RemoteItem.ParentReference.DriveId}/items/{oneDriveItem.RemoteItem.Id}";
        }
        else if (oneDriveItem.ParentReference != null && !string.IsNullOrEmpty(oneDriveItem.ParentReference.DriveId))
        {
            // Item to delete is shared from another drive
            completeUrl = $"drives/{oneDriveItem.ParentReference.DriveId}/items/{oneDriveItem.Id}";
        }
        else
        {
            // Item to delete resides on the current user its drive
            completeUrl = $"drive/items/{oneDriveItem.Id}";
        }

        return await DeleteItemInternal(completeUrl, cancellationToken);
    }

    /// <summary>
    /// Deletes the provided OneDriveItem from OneDrive
    /// </summary>
    /// <param name="oneDriveItemPath">The path to the OneDrive item to delete from OneDrive</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    public virtual async Task<bool> Delete(string oneDriveItemPath, CancellationToken cancellationToken = new())
    {
        return await DeleteItemInternal($"drive/root:/{oneDriveItemPath}", cancellationToken);
    }

    /// <summary>
    /// Copies the provided OneDriveItem to the provided destination on OneDrive
    /// </summary>
    /// <param name="oneDriveSourceItemPath">The path to the OneDrive Item to be copied</param>
    /// <param name="oneDriveDestinationItemPath">The path to the OneDrive parent item to copy the item into</param>
    /// <param name="destinationName">
    /// The name of the item at the destination where it will be copied to. Omit to use the
    /// source name.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    public virtual async Task<bool> Copy(string oneDriveSourceItemPath, string oneDriveDestinationItemPath, string destinationName = null, CancellationToken cancellationToken = new())
    {
        var oneDriveSourceItem = await GetItem(oneDriveSourceItemPath, cancellationToken);
        var oneDriveDestinationItem = await GetItem(oneDriveDestinationItemPath, cancellationToken);

        return await Copy(oneDriveSourceItem, oneDriveDestinationItem, destinationName, cancellationToken);
    }

    /// <summary>
    /// Copies the provided OneDriveItem to the provided destination on OneDrive
    /// </summary>
    /// <param name="oneDriveSourceItem">The path to the OneDrive Item to be copied</param>
    /// <param name="oneDriveDestinationItem">The path tothe OneDrive parent item to copy the item into</param>
    /// <param name="destinationName">
    /// The name of the item at the destination where it will be copied to. Omit to use the
    /// source name.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    public virtual async Task<bool> Copy(OneDriveItem oneDriveSourceItem, OneDriveItem oneDriveDestinationItem, string destinationName = null, CancellationToken cancellationToken = new())
    {
        return await CopyItemInternal(oneDriveSourceItem, oneDriveDestinationItem, destinationName, cancellationToken);
    }

    /// <summary>
    /// Moves the provided OneDriveItem to the provided destination on OneDrive
    /// </summary>
    /// <param name="oneDriveSourceItemPath">The path to the OneDrive Item to be moved</param>
    /// <param name="oneDriveDestinationItemPath">The path to the OneDrive parent item to move the item into</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    public virtual async Task<bool> Move(string oneDriveSourceItemPath, string oneDriveDestinationItemPath, CancellationToken cancellationToken = new())
    {
        var oneDriveSourceItem = await GetItem(oneDriveSourceItemPath, cancellationToken);
        var oneDriveDestinationItem = await GetItem(oneDriveDestinationItemPath, cancellationToken);

        return await Move(oneDriveSourceItem, oneDriveDestinationItem, cancellationToken);
    }

    /// <summary>
    /// Moves the provided OneDriveItem to the provided destination on OneDrive
    /// </summary>
    /// <param name="oneDriveSourceItem">The OneDrive Item to be moved</param>
    /// <param name="oneDriveDestinationItem">The OneDrive parent item to move the item into</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    public virtual async Task<bool> Move(OneDriveItem oneDriveSourceItem, OneDriveItem oneDriveDestinationItem, CancellationToken cancellationToken = new())
    {
        return await MoveItemInternal(oneDriveSourceItem, oneDriveDestinationItem, cancellationToken);
    }

    /// <summary>
    /// Renames the provided OneDriveItem to the provided name
    /// </summary>
    /// <param name="oneDriveItemPath">The path to the OneDrive Item to be renamed</param>
    /// <param name="name">The new name to assign to the OneDrive item</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    public virtual async Task<bool> Rename(string oneDriveItemPath, string name, CancellationToken cancellationToken = new())
    {
        var oneDriveItem = await GetItem(oneDriveItemPath, cancellationToken);

        return await Rename(oneDriveItem, name, cancellationToken);
    }

    /// <summary>
    /// Renames the provided OneDriveItem to the provided name
    /// </summary>
    /// <param name="oneDriveItemPath">The OneDrive Item to be renamed</param>
    /// <param name="name">The new name to assign to the OneDrive item</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    public virtual async Task<bool> Rename(OneDriveItem oneDriveItemPath, string name, CancellationToken cancellationToken = new())
    {
        return await RenameItemInternal(oneDriveItemPath, name, cancellationToken);
    }

    /// <summary>
    /// Downloads the contents of the item on OneDrive at the provided path to the folder provided keeping the original
    /// filename
    /// </summary>
    /// <param name="path">Path to an item on OneDrive to download its contents of</param>
    /// <param name="saveTo">
    /// Path where to save the file to. The same filename as used on OneDrive will be used to save the
    /// file under.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if download was successful, false if it failed</returns>
    public virtual async Task<bool> DownloadItem(string path, string saveTo, CancellationToken cancellationToken = new())
    {
        var oneDriveItem = await GetItem(path, cancellationToken);

        return await DownloadItem(oneDriveItem, saveTo, cancellationToken);
    }

    /// <summary>
    /// Downloads the contents of the provided OneDriveItem to the folder provided keeping the original filename
    /// </summary>
    /// <param name="oneDriveItem">OneDriveItem to download its contents of</param>
    /// <param name="saveTo">
    /// Path where to save the file to. The same filename as used on OneDrive will be used to save the
    /// file under.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if download was successful, false if it failed</returns>
    public virtual async Task<bool> DownloadItem(OneDriveItem oneDriveItem, string saveTo, CancellationToken cancellationToken = new())
    {
        return await DownloadItemAndSaveAs(oneDriveItem, Path.Combine(saveTo, oneDriveItem.Name), cancellationToken);
    }

    /// <summary>
    /// Downloads the contents of the item on OneDrive at the provided path to the full path provided
    /// </summary>
    /// <param name="path">Path to an item on OneDrive to download its contents of</param>
    /// <param name="saveAs">Full path including filename where to store the downloaded file</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if download was successful, false if it failed</returns>
    public virtual async Task<bool> DownloadItemAndSaveAs(string path, string saveAs, CancellationToken cancellationToken = new())
    {
        var oneDriveItem = await GetItem(path);

        return await DownloadItemAndSaveAs(oneDriveItem, saveAs, cancellationToken);
    }

    /// <summary>
    /// Downloads the contents of the provided OneDriveItem to the full path provided
    /// </summary>
    /// <param name="oneDriveItem">OneDriveItem to download its contents of</param>
    /// <param name="saveAs">Full path including filename where to store the downloaded file</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if download was successful, false if it failed</returns>
    public virtual async Task<bool> DownloadItemAndSaveAs(OneDriveItem oneDriveItem, string saveAs, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveItem.RemoteItem != null)
        {
            // Item to download is shared from another drive
            completeUrl = $"drives/{oneDriveItem.RemoteItem.ParentReference.DriveId}/items/{oneDriveItem.RemoteItem.Id}/content";
        }
        else if (oneDriveItem.ParentReference != null && !string.IsNullOrEmpty(oneDriveItem.ParentReference.DriveId))
        {
            // Item to download is shared from another drive
            completeUrl = $"drives/{oneDriveItem.ParentReference.DriveId}/items/{oneDriveItem.Id}/content";
        }
        else
        {
            // Item to download resides on the current user its drive
            completeUrl = $"drive/items/{oneDriveItem.Id}/content";
        }

        completeUrl = ConstructCompleteUrl(completeUrl);

        using (var stream = await DownloadItemInternal(oneDriveItem, completeUrl, cancellationToken))
        {
            using (var outputStream = new FileStream(saveAs, FileMode.Create))
            {
                await stream.CopyToAsync(outputStream, cancellationToken);
            }
        }

        return true;
    }

    /// <summary>
    /// Downloads the contents of the item on OneDrive at the provided path and returns the contents as a stream
    /// </summary>
    /// <param name="path">Path to an item on OneDrive to download its contents of</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Stream with the contents of the item on OneDrive</returns>
    public virtual async Task<Stream> DownloadItem(string path, CancellationToken cancellationToken = new())
    {
        var oneDriveItem = await GetItem(path, cancellationToken);

        return await DownloadItem(oneDriveItem, cancellationToken);
    }

    /// <summary>
    /// Downloads the contents of the provided OneDriveItem and returns the contents as a stream
    /// </summary>
    /// <param name="oneDriveItem">OneDriveItem to download its contents of</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Stream with the contents of the item on OneDrive</returns>
    public virtual async Task<Stream> DownloadItem(OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveItem.RemoteItem != null)
        {
            // Item to download is shared from another drive
            completeUrl = $"drives/{oneDriveItem.RemoteItem.ParentReference.DriveId}/items/{oneDriveItem.RemoteItem.Id}/content";
        }
        else if (oneDriveItem.ParentReference != null && !string.IsNullOrEmpty(oneDriveItem.ParentReference.DriveId))
        {
            // Item to download is shared from another drive
            completeUrl = $"drives/{oneDriveItem.ParentReference.DriveId}/items/{oneDriveItem.Id}/content";
        }
        else
        {
            // Item to download resides on the current user its drive
            completeUrl = $"drive/items/{oneDriveItem.Id}/content";
        }

        completeUrl = ConstructCompleteUrl(completeUrl);

        return await DownloadItemInternal(oneDriveItem, completeUrl, cancellationToken);
    }

    /// <summary>
    /// Uploads the provided file to OneDrive updating the original file
    /// </summary>
    /// <param name="filePath">Full path to the file to upload</param>
    /// <param name="oneDriveItem">OneDriveItem the item of which its contents should be updated</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
    public virtual async Task<OneDriveItem> UpdateFile(string filePath, OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        if (!File.Exists(filePath))
        {
            throw new ArgumentException("Provided file could not be found", nameof(filePath));
        }

        // Get a reference to the file to upload
        var fileToUpload = new FileInfo(filePath);

        // Verify if the filename does not contain any for OneDrive illegal characters
        if (!ValidFilename(fileToUpload.Name))
        {
            throw new ArgumentException("Provided file contains illegal characters in its filename", nameof(filePath));
        }

        // Verify which upload method should be used
        if (fileToUpload.Length <= MaximumBasicFileUploadSizeInBytes)
        {
            // Use the basic upload method                
            return await UpdateFileViaSimpleUpload(fileToUpload, oneDriveItem, cancellationToken);
        }

        // Use the resumable upload method
        return await UpdateFileViaResumableUpload(fileToUpload, oneDriveItem, null, cancellationToken);
    }

    /// <summary>
    /// Uploads the provided file to OneDrive keeping the original filename
    /// </summary>
    /// <param name="filePath">Full path to the file to upload</param>
    /// <param name="parentFolder">OneDriveItem of the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
    public virtual async Task<OneDriveItem> UploadFile(string filePath, OneDriveItem parentFolder, CancellationToken cancellationToken = new())
    {
        return await UploadFileAs(filePath, null, parentFolder, cancellationToken);
    }

    /// <summary>
    /// Uploads the provided file to OneDrive
    /// </summary>
    /// <param name="filePath">Full path to the file to upload</param>
    /// <param name="oneDriveFolder">Path to a OneDrive folder where to upload the file to</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
    public virtual async Task<OneDriveItem> UploadFile(string filePath, string oneDriveFolder, CancellationToken cancellationToken = new())
    {
        var oneDriveItem = await GetItem(oneDriveFolder, cancellationToken);

        return await UploadFile(filePath, oneDriveItem, cancellationToken);
    }

    /// <summary>
    /// Uploads the provided file to OneDrive using the provided filename
    /// </summary>
    /// <param name="filePath">Full path to the file to upload</param>
    /// <param name="fileName">Filename to assign to the file on OneDrive</param>
    /// <param name="oneDriveFolder">Path to a OneDrive folder where to upload the file to</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
    public virtual async Task<OneDriveItem> UploadFileAs(string filePath, string fileName, string oneDriveFolder, CancellationToken cancellationToken = new())
    {
        var oneDriveItem = await GetItem(oneDriveFolder, cancellationToken);

        return await UploadFileAs(filePath, fileName, oneDriveItem, cancellationToken);
    }

    /// <summary>
    /// Uploads the provided file to OneDrive using the provided filename
    /// </summary>
    /// <param name="fileStream">Stream to the file to upload</param>
    /// <param name="fileName">Filename to assign to the file on OneDrive</param>
    /// <param name="oneDriveFolder">Path to a OneDrive folder where to upload the file to</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
    public virtual async Task<OneDriveItem> UploadFileAs(Stream fileStream, string fileName, string oneDriveFolder, CancellationToken cancellationToken = new())
    {
        var oneDriveItem = await GetItem(oneDriveFolder, cancellationToken);

        return await UploadFileAs(fileStream, fileName, oneDriveItem, cancellationToken);
    }

    /// <summary>
    /// Uploads the provided file to OneDrive using the provided filename
    /// </summary>
    /// <param name="filePath">Full path to the file to upload</param>
    /// <param name="fileName">Filename to assign to the file on OneDrive</param>
    /// <param name="parentFolder">OneDriveItem of the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
    public virtual async Task<OneDriveItem> UploadFileAs(string filePath, string fileName, OneDriveItem parentFolder, CancellationToken cancellationToken = new())
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

        // Verify which upload method should be used
        if (fileToUpload.Length <= MaximumBasicFileUploadSizeInBytes)
        {
            // Use the basic upload method                
            return await UploadFileViaSimpleUpload(fileToUpload, fileName, parentFolder, cancellationToken);
        }

        // Use the resumable upload method
        return await UploadFileViaResumableUpload(fileToUpload, fileName, parentFolder, null, cancellationToken);
    }

    /// <summary>
    /// Uploads the provided file to OneDrive using the provided filename
    /// </summary>
    /// <param name="fileStream">Stream to the file to upload</param>
    /// <param name="fileName">Filename to assign to the file on OneDrive</param>
    /// <param name="parentFolder">OneDriveItem of the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem representing the uploaded file when successful or NULL when the upload failed</returns>
    public virtual async Task<OneDriveItem> UploadFileAs(Stream fileStream, string fileName, OneDriveItem parentFolder, CancellationToken cancellationToken = new())
    {
        if (fileStream == null || fileStream == Stream.Null)
        {
            throw new ArgumentNullException(nameof(fileStream));
        }

        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        if (parentFolder == null)
        {
            throw new ArgumentNullException(nameof(parentFolder));
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
            return await UploadFileViaSimpleUpload(fileStream, fileName, parentFolder, cancellationToken);
        }

        // Use the resumable upload method
        return await UploadFileViaResumableUpload(fileStream, fileName, parentFolder, null, cancellationToken);
    }

    /// <summary>
    /// Creates a new folder under the provided parent OneDrive item with the provided name
    /// </summary>
    /// <param name="parentPath">The path to the OneDrive folder under which the folder should be created</param>
    /// <param name="folderName">Name to assign to the new folder</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem entity representing the newly created folder or NULL if the operation fails</returns>
    public virtual async Task<OneDriveItem> CreateFolder(string parentPath, string folderName, CancellationToken cancellationToken = new())
    {
        return await CreateFolderInternal(!string.IsNullOrEmpty(parentPath) ? $"drive/root:/{parentPath}:/children" : "drive/root/children", folderName, cancellationToken);
    }

    /// <summary>
    /// Creates a new folder under the provided parent OneDrive item with the provided name
    /// </summary>
    /// <param name="parentItem">The OneDrive item under which the folder should be created</param>
    /// <param name="folderName">Name to assign to the new folder</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem entity representing the newly created folder or NULL if the operation fails</returns>
    public virtual async Task<OneDriveItem> CreateFolder(OneDriveItem parentItem, string folderName, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (parentItem.RemoteItem != null)
        {
            // Item where to create a new folder is shared from another drive
            completeUrl = $"drives/{parentItem.RemoteItem.ParentReference.DriveId}/items/{parentItem.RemoteItem.Id}/children";
        }

        else
        {
            // Item where to create a new folder resides on the current user its drive
            completeUrl = $"drive/items/{parentItem.Id}/children";
        }

        return await CreateFolderInternal(completeUrl, folderName, cancellationToken);
    }

    /// <summary>
    /// Shares a OneDrive item
    /// </summary>
    /// <param name="itemPath">The path to the OneDrive item to share</param>
    /// <param name="linkType">Type of sharing to request</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDrivePermission entity representing the share or NULL if the operation fails</returns>
    public virtual async Task<OneDrivePermission> ShareItem(string itemPath, OneDriveLinkType linkType, CancellationToken cancellationToken = new())
    {
        return await ShareItemInternal($"drive/root:/{itemPath}:/oneDrive.createLink", linkType, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Shares a OneDrive item
    /// </summary>
    /// <param name="item">The OneDrive item to share</param>
    /// <param name="linkType">Type of sharing to request</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDrivePermission entity representing the share or NULL if the operation fails</returns>
    public virtual async Task<OneDrivePermission> ShareItem(OneDriveItem item, OneDriveLinkType linkType, CancellationToken cancellationToken = new())
    {
        return await ShareItemInternal($"drive/items/{item.Id}/oneDrive.createLink", linkType, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns all the items that have been shared by others with the current user
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Collection with items that have been shared by others with the current user</returns>
    public virtual async Task<OneDriveItemCollection> GetSharedWithMe(CancellationToken cancellationToken = new())
    {
        var oneDriveItems = await GetData<OneDriveItemCollection>("drive/oneDrive.sharedWithMe", cancellationToken);

        return oneDriveItems;
    }

    /// <summary>
    /// Retrieves the first batch of children under the OneDrive folder with the provided id from the OneDrive with the
    /// provided id
    /// </summary>
    /// <param name="folderId">Id of the folder to retrieve the items of</param>
    /// <param name="driveId">Id of the drive on which the folder resides</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItemCollection containing the first batch of items in the folder</returns>
    public async Task<OneDriveItemCollection> GetChildrenFromDriveByFolderId(string driveId, string folderId, CancellationToken cancellationToken = new())
    {
        var oneDriveItems = await GetData<OneDriveItemCollection>($"drives/{driveId}/items/{folderId}/children", cancellationToken);

        return oneDriveItems;
    }

    /// <summary>
    /// Retrieves the first batch of children under the OneDrive folder with the provided id from the OneDrive with the
    /// provided id
    /// </summary>
    /// <param name="folder">Folder to retrieve the items of</param>
    /// <param name="drive">Drive on which the folder resides</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItemCollection containing the first batch of items in the folder</returns>
    public async Task<OneDriveItemCollection> GetChildrenFromDriveByFolderId(OneDriveDrive drive, OneDriveItem folder, CancellationToken cancellationToken = new())
    {
        var oneDriveItems = await GetChildrenFromDriveByFolderId(drive.Id, folder.Id, cancellationToken);

        return oneDriveItems;
    }

    /// <summary>
    /// Retrieves all the of children under the OneDrive folder with the provided id from the OneDrive with the provided id
    /// </summary>
    /// <param name="folderId">Id of the folder to retrieve the items of</param>
    /// <param name="driveId">Id of the drive on which the folder resides</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem array containing all items in the requested folder</returns>
    public virtual async Task<OneDriveItem[]> GetAllChildrenFromDriveByFolderId(string driveId, string folderId, CancellationToken cancellationToken = new())
    {
        return await GetAllChildrenInternal($"drives/{driveId}/items/{folderId}/children", cancellationToken);
    }

    /// <summary>
    /// Retrieves all the of children under the OneDrive folder with the provided id from the OneDrive with the provided id
    /// </summary>
    /// <param name="folder">Folder to retrieve the items of</param>
    /// <param name="drive">Drive on which the folder resides</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem array containing all items in the requested folder</returns>
    public virtual async Task<OneDriveItem[]> GetAllChildrenFromDriveByFolderId(OneDriveDrive drive, OneDriveItem folder, CancellationToken cancellationToken = new())
    {
        return await GetAllChildrenFromDriveByFolderId(drive.Id, folder.Id, cancellationToken);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Shares a OneDrive item
    /// </summary>
    /// <param name="oneDriveRequestUrl">The OneDrive request url which creates the share</param>
    /// <param name="linkType">Type of sharing to request</param>
    /// <param name="scope">Scope defining who has access to the shared item (not supported with OneDrive Personal)</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDrivePermission entity representing the share or NULL if the operation fails</returns>
    protected virtual async Task<OneDrivePermission> ShareItemInternal(string oneDriveRequestUrl, OneDriveLinkType linkType, OneDriveSharingScope? scope = null, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        var completeUrl = ConstructCompleteUrl(oneDriveRequestUrl);

        // Construct the OneDriveRequestShare entity with the sharing details
        var requestShare = new OneDriveRequestShare { SharingType = linkType, Scope = scope };

        // Call the OneDrive webservice
        var result = await SendMessageReturnOneDriveItem<OneDrivePermission>(requestShare, HttpMethod.Post, completeUrl, cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// Creates a new folder under the provided parent OneDrive item with the provided name
    /// </summary>
    /// <param name="oneDriveRequestUrl">The OneDrive request url which creates a new folder</param>
    /// <param name="folderName">Name to assign to the new folder</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem entity representing the newly created folder or NULL if the operation fails</returns>
    protected virtual async Task<OneDriveItem> CreateFolderInternal(string oneDriveRequestUrl, string folderName, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        var completeUrl = ConstructCompleteUrl(oneDriveRequestUrl);

        // Construct the JSON to send in the POST message
        var newFolder = new OneDriveCreateFolder { Name = folderName, Folder = new object() };

        // Send the webservice request
        var oneDriveItem = await SendMessageReturnOneDriveItem<OneDriveItem>(newFolder, HttpMethod.Post, completeUrl, HttpStatusCode.Created, cancellationToken);

        return oneDriveItem;
    }

    /// <summary>
    /// Searches OneDrive by calling the OneDrive API url as provided
    /// </summary>
    /// <param name="searchUrl">OneDrive API url representing the search to execute</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>List with OneDriveItem objects resulting from the search query</returns>
    protected async Task<IList<OneDriveItem>> SearchInternal(string searchUrl, CancellationToken cancellationToken = new())
    {
        // Create a list to contain all the search results
        var allResults = new List<OneDriveItem>();

        // Set the URL to execute against the OneDrive API to execute the query
        var nextSearchUrl = searchUrl;

        // Loop through the results for as long as there are more search results to return
        do
        {
            // Execute the search query against the OneDrive API
            var results = await GetData<OneDriveItemCollection>(nextSearchUrl, cancellationToken);

            // Add the retrieved results to the list
            allResults.AddRange(results.Collection);

            // Check if there are more search results
            if (results.NextLink == null)
            {
                // No more search results
                break;
            }

            // There are more search results. Use the link provided in the response to fetch the next results. Cut off the basic OneDrive API url.
            nextSearchUrl = results.NextLink.Remove(0, OneDriveApiBaseUrl.Length);
        } while (true);

        return allResults;
    }

    /// <summary>
    /// Downloads the contents of the provided OneDriveItem to the location provided
    /// </summary>
    /// <param name="item">OneDriveItem to download its contents of</param>
    /// <param name="completeUrl">Complete URL from where to download the file</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Stream with the downloaded content</returns>
    protected virtual async Task<Stream> DownloadItemInternal(OneDriveItem item, string completeUrl, CancellationToken cancellationToken = new())
    {
        // Get an access token to perform the request to OneDrive
        var accessToken = await GetAccessToken(cancellationToken);

        // Create an HTTPClient instance to communicate with the REST API of OneDrive
        var client = CreateHttpClient(accessToken.AccessToken);

        // Send the request to the OneDrive API
        var response = await client.GetAsync(completeUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // Download the file from OneDrive and return the stream
        var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        return downloadStream;
    }

    /// <summary>
    /// Performs a file upload to OneDrive overwriting an existing file using the simple OneDrive API. Best for small files
    /// on reliable network connections.
    /// </summary>
    /// <param name="filePath">File reference to the file to upload</param>
    /// <param name="oneDriveItem">OneDriveItem of the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>The resulting OneDrive item representing the uploaded file</returns>
    public async Task<OneDriveItem> UploadFileViaSimpleUpload(string filePath, OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        // Read the file to upload
        var fileInfo = new FileInfo(filePath);

        return await UpdateFileViaSimpleUpload(fileInfo, oneDriveItem, cancellationToken);
    }

    /// <summary>
    /// Performs a file upload to OneDrive overwriting an existing file using the simple OneDrive API. Best for small files
    /// on reliable network connections.
    /// </summary>
    /// <param name="file">FileInfo reference to the file to upload</param>
    /// <param name="oneDriveItem">OneDriveItem of the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>The resulting OneDrive item representing the uploaded file</returns>
    public async Task<OneDriveItem> UpdateFileViaSimpleUpload(FileInfo file, OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        using (var fileStream = file.OpenRead())
        {
            return await UpdateFileViaSimpleUpload(fileStream, oneDriveItem, cancellationToken);
        }
    }

    /// <summary>
    /// Performs a file upload to OneDrive overwriting an existing file using the simple OneDrive API. Best for small files
    /// on reliable network connections.
    /// </summary>
    /// <param name="fileStream">Stream to the file to upload</param>
    /// <param name="oneDriveItem">OneDriveItem of the existing item that should be updated on OneDrive</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>The resulting OneDrive item representing the uploaded file</returns>
    public async Task<OneDriveItem> UpdateFileViaSimpleUpload(Stream fileStream, OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveItem.RemoteItem != null)
        {
            // Item will be uploaded to another drive
            completeUrl = $"drives/{oneDriveItem.RemoteItem.ParentReference.DriveId}/items/{oneDriveItem.Id}/content";
        }
        else if (oneDriveItem.ParentReference != null && !string.IsNullOrEmpty(oneDriveItem.ParentReference.DriveId))
        {
            // Item will be uploaded to another drive
            completeUrl = $"drives/{oneDriveItem.ParentReference.DriveId}/items/{oneDriveItem.Id}/content";
        }
        else if (!string.IsNullOrEmpty(oneDriveItem.WebUrl) && oneDriveItem.WebUrl.Contains("cid="))
        {
            // Item will be uploaded to another drive. Used by OneDrive Personal when using a shared item.
            completeUrl = $"drives/{oneDriveItem.WebUrl.Remove(0, oneDriveItem.WebUrl.IndexOf("cid=", StringComparison.Ordinal) + 4)}/items/{oneDriveItem.Id}/content";
        }
        else
        {
            // Item will be uploaded to the current user its drive
            completeUrl = $"drive/items/{oneDriveItem.Id}/content";
        }

        completeUrl = ConstructCompleteUrl(completeUrl);

        return await UploadFileViaSimpleUploadInternal(fileStream, completeUrl, cancellationToken);
    }

    /// <summary>
    /// Performs a file upload to OneDrive using the simple OneDrive API. Best for small files on reliable network
    /// connections.
    /// </summary>
    /// <param name="fileStream">Stream to the file to upload</param>
    /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
    /// <param name="parentFolder">OneDriveItem of the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>The resulting OneDrive item representing the uploaded file</returns>
    public async Task<OneDriveItem> UploadFileViaSimpleUpload(Stream fileStream, string fileName, OneDriveItem parentFolder, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (parentFolder.RemoteItem != null)
        {
            // Item will be uploaded to another drive
            completeUrl = $"drives/{parentFolder.RemoteItem.ParentReference.DriveId}/items/{parentFolder.RemoteItem.Id}/children/{fileName}/content";
        }
        else if (parentFolder.ParentReference != null && !string.IsNullOrEmpty(parentFolder.ParentReference.DriveId))
        {
            // Item will be uploaded to another drive
            // Koen
            var existingItem = GetItemFromDriveById("", parentFolder.ParentReference.DriveId, cancellationToken);

            completeUrl = $"drives/{parentFolder.ParentReference.DriveId}/items/{parentFolder.Id}/children/{fileName}/content";
        }
        else if (!string.IsNullOrEmpty(parentFolder.WebUrl) && parentFolder.WebUrl.Contains("cid="))
        {
            // Item will be uploaded to another drive. Used by OneDrive Personal when using a shared item.
            completeUrl = $"drives/{parentFolder.WebUrl.Remove(0, parentFolder.WebUrl.IndexOf("cid=", StringComparison.Ordinal) + 4)}/items/{parentFolder.Id}/children/{fileName}/content";
        }
        else
        {
            // Item will be uploaded to the current user its drive
            completeUrl = $"drive/items/{parentFolder.Id}/children/{fileName}/content";
        }

        completeUrl = ConstructCompleteUrl(completeUrl);

        return await UploadFileViaSimpleUploadInternal(fileStream, completeUrl, cancellationToken);
    }

    /// <summary>
    /// Performs a file upload to OneDrive using the simple OneDrive API. Best for small files on reliable network
    /// connections.
    /// </summary>
    /// <param name="fileStream">Stream to the file to upload</param>
    /// <param name="oneDriveUrl">The URL to POST the file contents to</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>The resulting OneDrive item representing the uploaded file</returns>
    protected async Task<OneDriveItem> UploadFileViaSimpleUploadInternal(Stream fileStream, string oneDriveUrl, CancellationToken cancellationToken = new())
    {
        // Get an access token to perform the request to OneDrive
        var accessToken = await GetAccessToken(cancellationToken);

        // Create an HTTPClient instance to communicate with the REST API of OneDrive
        using (var client = CreateHttpClient(accessToken.AccessToken))
        {
            // Load the content to upload
            using (var content = new StreamContent(fileStream))
            {
                // Indicate that we're sending binary data
                content.Headers.Add("Content-Type", "application/octet-stream");

                // Construct the PUT message towards the webservice
                using (var request = new HttpRequestMessage(HttpMethod.Put, oneDriveUrl))
                {
                    // Set the content to upload
                    request.Content = content;

                    // Request the response from the webservice
                    using (var response = await client.SendAsync(request, cancellationToken))
                    {
                        // Read the response as a string
                        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

                        // Convert the JSON result to its appropriate type
                        try
                        {
                            var options = new JsonSerializerOptions();
                            options.Converters.Add(new JsonStringEnumConverter());

                            var responseOneDriveItem = JsonSerializer.Deserialize<OneDriveItem>(responseString, options);
                            responseOneDriveItem.OriginalJson = responseString;

                            return responseOneDriveItem;
                        }
                        catch (JsonException e)
                        {
                            throw new InvalidResponseException(responseString, e);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Performs a file upload to OneDrive using the simple OneDrive API. Best for small files on reliable network
    /// connections.
    /// </summary>
    /// <param name="file">File reference to the file to upload</param>
    /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
    /// <param name="oneDriveItem">OneDriveItem of the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>The resulting OneDrive item representing the uploaded file</returns>
    public async Task<OneDriveItem> UploadFileViaSimpleUpload(FileInfo file, string fileName, OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        // Read the file to upload
        using (var fileStream = file.OpenRead())
        {
            return await UploadFileViaSimpleUpload(fileStream, fileName, oneDriveItem, cancellationToken);
        }
    }

    /// <summary>
    /// Performs a file upload to OneDrive using the simple OneDrive API. Best for small files on reliable network
    /// connections.
    /// </summary>
    /// <param name="filePath">Path to the file to upload</param>
    /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
    /// <param name="oneDriveItem">OneDriveItem of the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>The resulting OneDrive item representing the uploaded file</returns>
    public async Task<OneDriveItem> UploadFileViaSimpleUpload(string filePath, string fileName, OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        var file = new FileInfo(filePath);

        return await UploadFileViaSimpleUpload(file, fileName, oneDriveItem, cancellationToken);
    }

    /// <summary>
    /// Uploads a file to OneDrive using the resumable method. Better for large files or unstable network connections.
    /// </summary>
    /// <param name="filePath">Path to the file to upload</param>
    /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
    /// <param name="parentFolder">OneDrive item representing the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem instance representing the uploaded item</returns>
    public async Task<OneDriveItem> UploadFileViaResumableUpload(string filePath, string fileName, OneDriveItem parentFolder, CancellationToken cancellationToken = new())
    {
        var file = new FileInfo(filePath);

        return await UploadFileViaResumableUpload(file, fileName, parentFolder, null, cancellationToken);
    }

    /// <summary>
    /// Uploads a file to OneDrive using the resumable file upload method
    /// </summary>
    /// <param name="file">FileInfo instance pointing to the file to upload</param>
    /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
    /// <param name="parentFolder">OneDrive item representing the folder to which the file should be uploaded</param>
    /// <param name="fragmentSizeInBytes">
    /// Size in bytes of the fragments to use for uploading. Higher numbers are faster but
    /// require more stable connections, lower numbers are slower but work better with unstable connections. Provide NULL
    /// to use the default.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem instance representing the uploaded item</returns>
    public virtual async Task<OneDriveItem> UploadFileViaResumableUpload(FileInfo file, string fileName, OneDriveItem parentFolder, long? fragmentSizeInBytes, CancellationToken cancellationToken = new())
    {
        // Open the source file for reading
        using (var fileStream = file.OpenRead())
        {
            return await UploadFileViaResumableUpload(fileStream, fileName, parentFolder, fragmentSizeInBytes, cancellationToken);
        }
    }

    /// <summary>
    /// Uploads a file to OneDrive using the resumable file upload method
    /// </summary>
    /// <param name="fileStream">Stream pointing to the file to upload</param>
    /// <param name="fileName">The filename under which the file should be stored on OneDrive</param>
    /// <param name="parentFolder">OneDrive item representing the folder to which the file should be uploaded</param>
    /// <param name="fragmentSizeInBytes">
    /// Size in bytes of the fragments to use for uploading. Higher numbers are faster but
    /// require more stable connections, lower numbers are slower but work better with unstable connections
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem instance representing the uploaded item</returns>
    public virtual async Task<OneDriveItem> UploadFileViaResumableUpload(Stream fileStream, string fileName, OneDriveItem parentFolder, long? fragmentSizeInBytes, CancellationToken cancellationToken = new())
    {
        var oneDriveUploadSession = await CreateResumableUploadSession(fileName, parentFolder, cancellationToken);

        return await UploadFileViaResumableUploadInternal(fileStream, oneDriveUploadSession, fragmentSizeInBytes, cancellationToken);
    }

    /// <summary>
    /// Uploads a file to OneDrive updating the contents of an existing file using the resumable method. Better for large
    /// files or unstable network connections.
    /// </summary>
    /// <param name="filePath">Path to the file to upload</param>
    /// <param name="oneDriveItem">OneDrive item representing the folder to which the file should be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem instance representing the uploaded item</returns>
    public async Task<OneDriveItem> UpdateFileViaResumableUpload(string filePath, OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        var file = new FileInfo(filePath);

        return await UpdateFileViaResumableUpload(file, oneDriveItem, null, cancellationToken);
    }

    /// <summary>
    /// Uploads a file to OneDrive updating the contents of an existing file using the resumable file upload method
    /// </summary>
    /// <param name="file">FileInfo instance pointing to the file to upload</param>
    /// <param name="oneDriveItem">OneDrive item representing the folder to which the file should be uploaded</param>
    /// <param name="fragmentSizeInBytes">
    /// Size in bytes of the fragments to use for uploading. Higher numbers are faster but
    /// require more stable connections, lower numbers are slower but work better with unstable connections. Provide NULL
    /// to use the default.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem instance representing the uploaded item</returns>
    public virtual async Task<OneDriveItem> UpdateFileViaResumableUpload(FileInfo file, OneDriveItem oneDriveItem, long? fragmentSizeInBytes, CancellationToken cancellationToken = new())
    {
        // Open the source file for reading
        using (var fileStream = file.OpenRead())
        {
            return await UpdateFileViaResumableUpload(fileStream, oneDriveItem, fragmentSizeInBytes, cancellationToken);
        }
    }

    /// <summary>
    /// Uploads a file to OneDrive updating the contents of an existing file using the resumable file upload method
    /// </summary>
    /// <param name="fileStream">Stream pointing to the file to upload</param>
    /// <param name="oneDriveItem">OneDrive item representing the folder to which the file should be uploaded</param>
    /// <param name="fragmentSizeInBytes">
    /// Size in bytes of the fragments to use for uploading. Higher numbers are faster but
    /// require more stable connections, lower numbers are slower but work better with unstable connections
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem instance representing the uploaded item</returns>
    public virtual async Task<OneDriveItem> UpdateFileViaResumableUpload(Stream fileStream, OneDriveItem oneDriveItem, long? fragmentSizeInBytes, CancellationToken cancellationToken = new())
    {
        var oneDriveUploadSession = await CreateResumableUploadSession(oneDriveItem, cancellationToken);

        return await UploadFileViaResumableUploadInternal(fileStream, oneDriveUploadSession, fragmentSizeInBytes, cancellationToken);
    }

    /// <summary>
    /// Uploads a file to OneDrive using the resumable file upload method
    /// </summary>
    /// <param name="fileStream">Stream pointing to the file to upload</param>
    /// <param name="oneDriveUploadSession">Upload session under which the upload will be performed</param>
    /// <param name="fragmentSizeInBytes">
    /// Size in bytes of the fragments to use for uploading. Higher numbers are faster but
    /// require more stable connections, lower numbers are slower but work better with unstable connections.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveItem instance representing the uploaded item</returns>
    protected virtual async Task<OneDriveItem> UploadFileViaResumableUploadInternal(Stream fileStream, OneDriveUploadSession oneDriveUploadSession, long? fragmentSizeInBytes, CancellationToken cancellationToken = new())
    {
        if (fileStream == null)
        {
            throw new ArgumentNullException("fileStream");
        }

        if (oneDriveUploadSession == null)
        {
            throw new ArgumentNullException("oneDriveUploadSession");
        }

        // Get an access token to perform the request to OneDrive
        var accessToken = await GetAccessToken();

        // Amount of bytes succesfully sent
        long totalBytesSent = 0;

        // Used for retrying failed transmissions
        var transferAttemptCount = 0;
        const int transferMaxAttempts = 3;

        do
        {
            // Keep a counter how many times it has been attempted to send this file
            transferAttemptCount++;

            // Start sending the file from the first byte
            long currentPosition = 0;

            // Defines a buffer which will be filled with bytes from the original file and then sent off to the OneDrive webservice
            var fragmentBuffer = new byte[fragmentSizeInBytes ?? ResumableUploadChunkSizeInBytes];

            // Create an HTTPClient instance to communicate with the REST API of OneDrive to perform the upload 
            using (var client = CreateHttpClient(accessToken.AccessToken))
            {
                // Keep looping through the source file length until we've sent all bytes to the OneDrive webservice
                while (currentPosition < fileStream.Length)
                {
                    var fragmentSuccessful = true;

                    // Define the end position in the file bytes based on the buffer size we're using to send fragments of the file to OneDrive
                    var endPosition = currentPosition + fragmentBuffer.LongLength;

                    // Make sure our end position isn't further than the file size in which case it would be the last fragment of the file to be sent
                    if (endPosition > fileStream.Length)
                    {
                        endPosition = fileStream.Length;
                    }

                    // Define how many bytes should be read from the source file
                    var amountOfBytesToSend = (int)(endPosition - currentPosition);

                    // Copy the bytes from the source file into the buffer
                    await fileStream.ReadExactlyAsync(fragmentBuffer, 0, amountOfBytesToSend, cancellationToken);

                    // Load the content to upload
                    using (var content = new ByteArrayContent(fragmentBuffer, 0, amountOfBytesToSend))
                    {
                        // Indicate that we're sending binary data
                        content.Headers.Add("Content-Type", "application/octet-stream");

                        // Provide information to OneDrive which range of bytes we're going to send and the total amount of bytes the file exists out of
                        content.Headers.Add("Content-Range", $"bytes {currentPosition}-{endPosition - 1}/{fileStream.Length}");

                        // Construct the PUT message towards the webservice containing the binary data
                        using (var request = new HttpRequestMessage(HttpMethod.Put, oneDriveUploadSession.UploadUrl))
                        {
                            // Set the binary content to upload
                            request.Content = content;

                            // Send the data to the webservice
                            using (var response = await client.SendAsync(request, cancellationToken))
                            {
                                // Check the response code
                                switch (response.StatusCode)
                                {
                                    // Fragment has been received, awaiting next fragment
                                    case HttpStatusCode.Accepted:
                                        // Move the current position pointer to the end of the fragment we've just sent so we continue from there with the next upload
                                        currentPosition = endPosition;
                                        totalBytesSent += amountOfBytesToSend;

                                        // Trigger event
                                        UploadProgressChanged?.Invoke(this, new OneDriveUploadProgressChangedEventArgs(totalBytesSent, fileStream.Length));

                                        break;
                                    case HttpStatusCode.Unauthorized:
                                        throw new ApplicationException("unauthorized");
                                    // All fragments have been received, the file did already exist and has been overwritten
                                    case HttpStatusCode.OK:
                                    // All fragments have been received, the file has been created
                                    case HttpStatusCode.Created:
                                        // Read the response as a string
                                        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

                                        // Convert the JSON result to its appropriate type
                                        try
                                        {
                                            var options = new JsonSerializerOptions();
                                            options.Converters.Add(new JsonStringEnumConverter());

                                            var responseOneDriveItem = JsonSerializer.Deserialize<OneDriveItem>(responseString, options);
                                            responseOneDriveItem.OriginalJson = responseString;

                                            return responseOneDriveItem;
                                        }
                                        catch (JsonException e)
                                        {
                                            throw new InvalidResponseException(responseString, e);
                                        }

                                    // All other status codes are considered to indicate a failed fragment transmission and will be retried
                                    default:
                                        fragmentSuccessful = false;

                                        break;
                                }
                            }
                        }
                    }

                    // Check if the fragment was successful, if not, retry the complete upload
                    if (!fragmentSuccessful)
                    {
                        break;
                    }
                }
            }
        } while (transferAttemptCount < transferMaxAttempts);

        // Request failed
        return null;
    }

    /// <summary>
    /// Initiates a resumable upload session to OneDrive to overwrite an existing file. It doesn't perform the actual
    /// upload yet.
    /// </summary>
    /// <param name="oneDriveItem">OneDriveItem item for which updated content will be uploaded</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDriveUploadSession instance containing the details where to upload the content to</returns>
    protected virtual async Task<OneDriveUploadSession> CreateResumableUploadSession(OneDriveItem oneDriveItem, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveItem.RemoteItem != null)
        {
            // Item will be uploaded to another drive
            completeUrl = $"drives/{oneDriveItem.RemoteItem.ParentReference.DriveId}/items/{oneDriveItem.RemoteItem.Id}/upload.createSession";
        }
        else if (oneDriveItem.ParentReference != null && !string.IsNullOrEmpty(oneDriveItem.ParentReference.DriveId))
        {
            // Item will be uploaded to another drive
            completeUrl = $"drives/{oneDriveItem.ParentReference.DriveId}/items/{oneDriveItem.Id}/upload.createSession";
        }
        else if (!string.IsNullOrEmpty(oneDriveItem.WebUrl) && oneDriveItem.WebUrl.Contains("cid="))
        {
            // Item will be uploaded to another drive. Used by OneDrive Personal when using a shared item.
            completeUrl = $"drives/{oneDriveItem.WebUrl.Remove(0, oneDriveItem.WebUrl.IndexOf("cid=") + 4)}/items/{oneDriveItem.Id}/upload.createSession";
        }
        else
        {
            // Item will be uploaded to the current user its drive
            completeUrl = $"drive/items/{oneDriveItem.Id}/upload.createSession";
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
    protected virtual async Task<OneDriveUploadSession> CreateResumableUploadSession(string fileName, OneDriveItem oneDriveFolder, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveFolder.RemoteItem != null)
        {
            // Item will be uploaded to another drive
            completeUrl = $"drives/{oneDriveFolder.RemoteItem.ParentReference.DriveId}/items/{oneDriveFolder.RemoteItem.Id}:/{fileName}:/upload.createSession";
        }
        else if (oneDriveFolder.ParentReference != null && !string.IsNullOrEmpty(oneDriveFolder.ParentReference.DriveId))
        {
            // Item will be uploaded to another drive
            completeUrl = $"drives/{oneDriveFolder.ParentReference.DriveId}/items/{oneDriveFolder.Id}:/{fileName}:/upload.createSession";
        }
        else if (!string.IsNullOrEmpty(oneDriveFolder.WebUrl) && oneDriveFolder.WebUrl.Contains("cid="))
        {
            // Item will be uploaded to another drive. Used by OneDrive Personal when using a shared item.
            completeUrl = $"drives/{oneDriveFolder.WebUrl.Remove(0, oneDriveFolder.WebUrl.IndexOf("cid=") + 4)}/items/{oneDriveFolder.Id}:/{fileName}:/upload.createSession";
        }
        else
        {
            // Item will be uploaded to the current user its drive
            completeUrl = $"drive/items/{oneDriveFolder.Id}:/{fileName}:/upload.createSession";
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
    /// Retrieves data from the OneDrive API
    /// </summary>
    /// <typeparam name="T">Type of OneDrive entity to expect to be returned</typeparam>
    /// <param name="url">Url fragment after the OneDrive base Uri which indicated the type of information to return</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>OneDrive entity filled with the information retrieved from the OneDrive API</returns>
    protected virtual async Task<T> GetData<T>(string url, CancellationToken cancellationToken = new()) where T : OneDriveItemBase
    {
        // Construct the complete URL to call
        var completeUrl = ConstructCompleteUrl(url);

        // Call the OneDrive webservice
        var result = await SendMessageReturnOneDriveItem<T>("", HttpMethod.Get, completeUrl, HttpStatusCode.OK, cancellationToken);

        return result;
    }

    /// <summary>
    /// Sends a HTTP DELETE to OneDrive to delete a file
    /// </summary>
    /// <param name="oneDriveUrl">The OneDrive API url to call to delete an item</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    protected virtual async Task<bool> DeleteItemInternal(string oneDriveUrl, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        var completeUrl = ConstructCompleteUrl(oneDriveUrl);

        // Call the OneDrive webservice
        var result = await SendMessageReturnBool(null, HttpMethod.Delete, completeUrl, HttpStatusCode.NoContent, cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// Sends a HTTP POST to OneDrive to copy an item on OneDrive
    /// </summary>
    /// <param name="oneDriveSource">The OneDrive Item to be copied</param>
    /// <param name="oneDriveDestinationParent">The OneDrive parent item to copy the item into</param>
    /// <param name="destinationName">The name of the item at the destination where it will be copied to</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    protected virtual async Task<bool> CopyItemInternal(OneDriveItem oneDriveSource, OneDriveItem oneDriveDestinationParent, string destinationName, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveSource.RemoteItem != null)
        {
            // Item to copy is shared from another drive
            completeUrl = $"drives/{oneDriveSource.RemoteItem.ParentReference.DriveId}/items/{oneDriveSource.RemoteItem.Id}/action.copy";
        }
        else
        {
            // Item to copy resides on the current user its drive
            completeUrl = $"drive/items/{oneDriveSource.Id}/action.copy";
        }

        completeUrl = ConstructCompleteUrl(completeUrl);

        // Construct the OneDriveParentItemReference entity with the item to be copied details
        var requestBody = new OneDriveParentItemReference
        {
            ParentReference = new OneDriveItemReference
            {
                Id = oneDriveDestinationParent.Id,
                DriveId = oneDriveDestinationParent.ParentReference.DriveId
            },
            Name = destinationName
        };

        // Call the OneDrive webservice
        var result = await SendMessageReturnBool(requestBody, HttpMethod.Post, completeUrl, HttpStatusCode.Accepted, true, cancellationToken);

        return result;
    }

    /// <summary>
    /// Sends a HTTP PATCH to OneDrive to move an item on OneDrive
    /// </summary>
    /// <param name="oneDriveSource">The OneDrive Item to be moved</param>
    /// <param name="oneDriveDestinationParent">The OneDrive parent item to move the item to</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    protected virtual async Task<bool> MoveItemInternal(OneDriveItem oneDriveSource, OneDriveItem oneDriveDestinationParent, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveSource.RemoteItem != null)
        {
            // Item to copy is shared from another drive
            completeUrl = $"drives/{oneDriveSource.RemoteItem.ParentReference.DriveId}/items/{oneDriveSource.RemoteItem.Id}";
        }
        else
        {
            // Item to copy resides on the current user its drive
            completeUrl = $"drive/items/{oneDriveSource.Id}";
        }

        completeUrl = ConstructCompleteUrl(completeUrl);

        // Construct the OneDriveParentItemReference entity with the item to be moved details
        var requestBody = new OneDriveParentItemReference
        {
            ParentReference = new OneDriveItemReference
            {
                Id = oneDriveDestinationParent.Id,
                DriveId = oneDriveDestinationParent.ParentReference.DriveId
            }
        };

        // Call the OneDrive webservice
        var result = await SendMessageReturnBool(requestBody, new HttpMethod("PATCH"), completeUrl, HttpStatusCode.OK, cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// Sends a HTTP PATCH to OneDrive to rename an item on OneDrive
    /// </summary>
    /// <param name="oneDriveSource">The OneDrive Item to be renamed</param>
    /// <param name="name">The new name to give to the OneDrive item</param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>True if successful, false if failed</returns>
    protected virtual async Task<bool> RenameItemInternal(OneDriveItem oneDriveSource, string name, CancellationToken cancellationToken = new())
    {
        // Construct the complete URL to call
        string completeUrl;

        if (oneDriveSource.RemoteItem != null)
        {
            // Item to copy is shared from another drive
            completeUrl = $"drives/{oneDriveSource.RemoteItem.ParentReference.DriveId}/items/{oneDriveSource.RemoteItem.Id}";
        }
        else
        {
            // Item to copy resides on the current user its drive
            completeUrl = $"drive/items/{oneDriveSource.Id}";
        }

        completeUrl = ConstructCompleteUrl(completeUrl);

        // Construct the OneDriveItem entity with the item to be renamed details
        var requestBody = new OneDriveItem
        {
            Name = name
        };

        // Call the OneDrive webservice
        var result = await SendMessageReturnBool(requestBody, new HttpMethod("PATCH"), completeUrl, HttpStatusCode.OK, cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// Sends a message to the OneDrive webservice and returns a OneDriveBaseItem with the response
    /// </summary>
    /// <typeparam name="T">OneDriveBaseItem type of the expected response</typeparam>
    /// <param name="oneDriveItem">OneDriveBaseItem of the message to send to the webservice</param>
    /// <param name="httpMethod">HttpMethod to use to send with the webservice (i.e. POST, GET, PUT, etc.)</param>
    /// <param name="url">Url of the OneDrive webservice to send the message to</param>
    /// <param name="expectedHttpStatusCode">
    /// The expected Http result status code. Optional. If provided and the webservice
    /// returns a different response, the return type will be NULL to indicate failure.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Typed OneDrive entity with the result from the webservice</returns>
    protected virtual async Task<T> SendMessageReturnOneDriveItem<T>(OneDriveItemBase oneDriveItem, HttpMethod httpMethod, string url, HttpStatusCode? expectedHttpStatusCode = null, CancellationToken cancellationToken = new()) where T : OneDriveItemBase
    {
        var bodyText = oneDriveItem != null ? JsonSerializer.Serialize(oneDriveItem, oneDriveItem.GetType(), JSONOptions) : null;

        return await SendMessageReturnOneDriveItem<T>(bodyText, httpMethod, url, expectedHttpStatusCode, cancellationToken);
    }

    /// <summary>
    /// Sends a message to the OneDrive webservice and returns a OneDriveBaseItem with the response
    /// </summary>
    /// <typeparam name="T">OneDriveBaseItem type of the expected response</typeparam>
    /// <param name="bodyText">String with the message to send to the webservice</param>
    /// <param name="httpMethod">HttpMethod to use to send with the webservice (i.e. POST, GET, PUT, etc.)</param>
    /// <param name="url">Url of the OneDrive webservice to send the message to</param>
    /// <param name="expectedHttpStatusCode">
    /// The expected Http result status code. Optional. If provided and the webservice
    /// returns a different response, the return type will be NULL to indicate failure.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Typed OneDrive entity with the result from the webservice</returns>
    protected virtual async Task<T> SendMessageReturnOneDriveItem<T>(string bodyText, HttpMethod httpMethod, string url, HttpStatusCode? expectedHttpStatusCode = null, CancellationToken cancellationToken = new()) where T : OneDriveItemBase
    {
        var responseString = await SendMessageReturnString(bodyText, httpMethod, url, expectedHttpStatusCode, cancellationToken);

        // Validate output was generated
        if (string.IsNullOrEmpty(responseString))
        {
            return null;
        }

        // Convert the JSON result to its appropriate type
        try
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());

            var responseOneDriveItem = JsonSerializer.Deserialize<T>(responseString, options);
            responseOneDriveItem.OriginalJson = responseString;

            return responseOneDriveItem;
        }
        catch (JsonException e)
        {
            throw new InvalidResponseException(responseString, e);
        }
    }

    /// <summary>
    /// Sends a message to the OneDrive webservice and returns a string with the response
    /// </summary>
    /// <param name="bodyText">String with the message to send to the webservice</param>
    /// <param name="httpMethod">HttpMethod to use to send with the webservice (i.e. POST, GET, PUT, etc.)</param>
    /// <param name="url">Url of the OneDrive webservice to send the message to</param>
    /// <param name="expectedHttpStatusCode">
    /// The expected Http result status code. Optional. If provided and the webservice
    /// returns a different response, the return type will be NULL to indicate failure.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>String containing the response of the webservice</returns>
    protected virtual async Task<string> SendMessageReturnString(string bodyText, HttpMethod httpMethod, string url, HttpStatusCode? expectedHttpStatusCode = null, CancellationToken cancellationToken = new())
    {
        using (var response = await SendMessageReturnHttpResponse(bodyText, httpMethod, url, cancellationToken: cancellationToken))
        {
            if (!expectedHttpStatusCode.HasValue || (expectedHttpStatusCode.HasValue && response != null && response.StatusCode == expectedHttpStatusCode.Value))
            {
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

                return responseString;
            }

            return null;
        }
    }

    /// <summary>
    /// Sends a message to the OneDrive webservice and returns a bool indicating if the response matched the expected HTTP
    /// status code result
    /// </summary>
    /// <param name="oneDriveItem">OneDriveBaseItem of the message to send to the webservice</param>
    /// <param name="httpMethod">HttpMethod to use to send with the webservice (i.e. POST, GET, PUT, etc.)</param>
    /// <param name="url">Url of the OneDrive webservice to send the message to</param>
    /// <param name="expectedHttpStatusCode">
    /// The expected Http result status code. Optional. If provided and the webservice
    /// returns a different response, the return type will be NULL to indicate failure.
    /// </param>
    /// <param name="preferRespondAsync">
    /// Provide true if the Prefer Async header should be sent along with the request. This is
    /// required for some requests. Optional, default = false = do not send the async header.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>Bool indicating if the HTTP response status from the webservice matched the provided expectedHttpStatusCode</returns>
    protected virtual async Task<bool> SendMessageReturnBool(OneDriveItemBase oneDriveItem, HttpMethod httpMethod, string url, HttpStatusCode expectedHttpStatusCode, bool preferRespondAsync = false, CancellationToken cancellationToken = new())
    {
        var bodyText = oneDriveItem != null ? JsonSerializer.Serialize(oneDriveItem, oneDriveItem.GetType(), JSONOptions) : null;

        using (var response = await SendMessageReturnHttpResponse(bodyText, httpMethod, url, preferRespondAsync, cancellationToken))
        {
            return response != null && response.StatusCode == expectedHttpStatusCode;
        }
    }

    /// <summary>
    /// Sends a message to the OneDrive webservice and returns the HttpResponse instance
    /// </summary>
    /// <param name="bodyText">String with the message to send to the webservice</param>
    /// <param name="httpMethod">HttpMethod to use to send with the webservice (i.e. POST, GET, PUT, etc.)</param>
    /// <param name="url">Url of the OneDrive webservice to send the message to</param>
    /// <param name="preferRespondAsync">
    /// Provide true if the Prefer Async header should be sent along with the request. This is
    /// required for some requests. Optional, default = false = do not send the async header.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token (optional)</param>
    /// <returns>HttpResponse of the webservice call. Note that the caller needs to dispose the returned instance.</returns>
    protected virtual async Task<HttpResponseMessage> SendMessageReturnHttpResponse(string bodyText, HttpMethod httpMethod, string url, bool preferRespondAsync = false, CancellationToken cancellationToken = new())
    {
        // Get an access token to perform the request to OneDrive
        var accessToken = await GetAccessToken(cancellationToken);

        // Create an HTTPClient instance to communicate with the REST API of OneDrive
        using (var client = CreateHttpClient(accessToken.AccessToken))
        {
            // Load the content to upload
            using (var content = new StringContent(bodyText ?? "", Encoding.UTF8, "application/json"))
            {
                // Construct the message towards the webservice
                using (var request = new HttpRequestMessage(httpMethod, url))
                {
                    if (preferRespondAsync)
                    {
                        // Add a header to prefer the operation to happen while we continue processing our code
                        request.Headers.Add("Prefer", "respond-async");
                    }

                    // Check if a body to send along with the request has been provided
                    if (!string.IsNullOrEmpty(bodyText) && httpMethod != HttpMethod.Get)
                    {
                        // Set the content to send along in the message body with the request
                        request.Content = content;
                    }

                    // Request the response from the webservice
                    var response = await client.SendAsync(request, cancellationToken);

                    return response;
                }
            }
        }
    }

    /// <summary>
    /// Instantiates a new HttpClient preconfigured for use. Note that the caller is responsible for disposing this object.
    /// </summary>
    /// <param name="bearerToken">Bearer token to add to the HTTP Client for authorization (optional)</param>
    /// <returns>HttpClient instance</returns>
    protected HttpClient CreateHttpClient(string bearerToken = null)
    {
        // Define the HttpClient settings
        var httpClientHandler = new HttpClientHandler
        {
            UseDefaultCredentials = ProxyCredential == null,
            UseProxy = ProxyConfiguration != null,
            Proxy = ProxyConfiguration
        };

        // Check if we need specific credentials for the proxy
        if (ProxyCredential != null && httpClientHandler.Proxy != null)
        {
            httpClientHandler.Proxy.Credentials = ProxyCredential;
        }

        // Create the new HTTP Client
        var httpClient = new HttpClient(httpClientHandler);

        if (!string.IsNullOrEmpty(bearerToken))
        {
            // Provide the access token through a bearer authorization header
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", bearerToken);
        }

        return httpClient;
    }

    /// <summary>
    /// Constructs the complete Url to be called based on the part of the url provided that contains the command
    /// </summary>
    /// <param name="commandUrl">Part of the URL to call that contains the command to execute for the API that is being called</param>
    /// <returns>Full URL to call the API</returns>
    protected virtual string ConstructCompleteUrl(string commandUrl)
    {
        // Check if the commandUrl is already a full URL, if so leave it as is. If not, prepend it with the Api Base URL.
        return commandUrl.StartsWith("http", StringComparison.InvariantCultureIgnoreCase) ? commandUrl : string.Concat(OneDriveApiBaseUrl, commandUrl);
    }

    #endregion
}