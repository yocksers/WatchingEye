using MediaBrowser.Model.Services;
using System;
using WatchingEye.Api;
using WatchingEye.Services;

namespace WatchingEye
{
    public class PublicApiService : IService, IRequiresRequest
    {
        private const string ApiKeyHeader = "X-WatchingEye-Api-Key";
        public IRequest Request { get; set; } = default!;

        private void EnsureApiKeyValid()
        {
            var genericError = "Unauthorized: Invalid API Key or configuration.";

            if (Plugin.Instance == null)
            {
                throw new UnauthorizedAccessException(genericError);
            }

            var apiKey = Plugin.Instance.Configuration.ApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new UnauthorizedAccessException(genericError);
            }

            if (Request == null)
            {
                throw new InvalidOperationException("The IRequest context was not injected into the API service.");
            }

            var providedKey = Request.Headers.Get(ApiKeyHeader);

            if (string.IsNullOrWhiteSpace(providedKey) || !string.Equals(providedKey, apiKey))
            {
                throw new UnauthorizedAccessException(genericError);
            }
        }

        public object Get(PublicGetLimitedUsersStatusRequest request)
        {
            EnsureApiKeyValid();
            return WatchTimeManager.GetLimitedUsersStatus();
        }

        public void Post(PublicExtendTimeRequest request)
        {
            EnsureApiKeyValid();

            if (string.IsNullOrEmpty(request.UserId) || request.Minutes <= 0)
            {
                throw new ArgumentException("Bad Request: UserId and a positive number of minutes are required.");
            }
            WatchTimeManager.ExtendTimeForUser(request.UserId, request.Minutes);
        }

        public void Post(PublicTimeOutUserRequest request)
        {
            EnsureApiKeyValid();

            if (string.IsNullOrEmpty(request.UserId) || request.Minutes <= 0)
            {
                throw new ArgumentException("Bad Request: UserId and a positive number of minutes are required.");
            }
            WatchTimeManager.TimeOutUser(request.UserId, request.Minutes);
        }

        public void Post(PublicClearTimeOutRequest request)
        {
            EnsureApiKeyValid();

            if (string.IsNullOrEmpty(request.UserId))
            {
                throw new ArgumentException("Bad Request: UserId is required.");
            }
            WatchTimeManager.ClearTimeOutForUser(request.UserId);
        }

        public void Post(PublicResetUserTimeRequest request)
        {
            EnsureApiKeyValid();

            if (string.IsNullOrEmpty(request.UserId))
            {
                throw new ArgumentException("Bad Request: UserId is required.");
            }
            WatchTimeManager.ResetWatchTimeForUser(request.UserId);
        }
    }
}