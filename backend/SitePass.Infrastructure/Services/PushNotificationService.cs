using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebPush;

namespace SitePass.Infrastructure.Services
{
    public class VapidKeysModel
    {
        public string PublicKey { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
    }

    public class SavedSubscription
    {
        public int UserId { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PushNotificationService
    {
        private readonly string _keysFilePath;
        private readonly string _subsFilePath;
        private VapidKeysModel _vapidKeys;
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public PushNotificationService()
        {
            // Save inside backend app directory
            var baseDir = AppContext.BaseDirectory;
            _keysFilePath = Path.Combine(baseDir, "vapid_keys.json");
            _subsFilePath = Path.Combine(baseDir, "push_subscriptions.json");
            
            _vapidKeys = InitializeVapidKeys();
        }

        private VapidKeysModel InitializeVapidKeys()
        {
            _lock.Wait();
            try
            {
                if (File.Exists(_keysFilePath))
                {
                    var json = File.ReadAllText(_keysFilePath);
                    var keys = JsonSerializer.Deserialize<VapidKeysModel>(json);
                    if (keys != null && !string.IsNullOrEmpty(keys.PublicKey))
                    {
                        return keys;
                    }
                }

                // Generate new keys using WebPush library
                var newKeys = VapidHelper.GenerateVapidKeys();
                var model = new VapidKeysModel
                {
                    PublicKey = newKeys.PublicKey,
                    PrivateKey = newKeys.PrivateKey
                };

                var serialized = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_keysFilePath, serialized);
                
                Console.WriteLine($"[VAPID] Generated and saved new VAPID keys to {_keysFilePath}");
                return model;
            }
            finally
            {
                _lock.Release();
            }
        }

        public string GetPublicKey()
        {
            return _vapidKeys.PublicKey;
        }

        public async Task SaveSubscriptionAsync(int userId, string endpoint, string p256dh, string auth)
        {
            await _lock.WaitAsync();
            try
            {
                List<SavedSubscription> subs = new();
                if (File.Exists(_subsFilePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(_subsFilePath);
                        subs = JsonSerializer.Deserialize<List<SavedSubscription>>(json) ?? new();
                    }
                    catch
                    {
                        subs = new();
                    }
                }

                // Remove duplicate subscription if it already exists for the same user & endpoint
                subs.RemoveAll(s => s.UserId == userId && s.Endpoint == endpoint);

                subs.Add(new SavedSubscription
                {
                    UserId = userId,
                    Endpoint = endpoint,
                    P256dh = p256dh,
                    Auth = auth
                });

                var serialized = JsonSerializer.Serialize(subs, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_subsFilePath, serialized);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SendNotificationToUserAsync(int userId, string title, string message)
        {
            List<SavedSubscription> subs = new();
            await _lock.WaitAsync();
            try
            {
                if (File.Exists(_subsFilePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(_subsFilePath);
                        subs = JsonSerializer.Deserialize<List<SavedSubscription>>(json) ?? new();
                    }
                    catch
                    {
                        subs = new();
                    }
                }
            }
            finally
            {
                _lock.Release();
            }

            var userSubs = subs.Where(s => s.UserId == userId).ToList();
            if (!userSubs.Any()) return;

            var vapidDetails = new VapidDetails("mailto:admin@sitepass.com", _vapidKeys.PublicKey, _vapidKeys.PrivateKey);
            var webPushClient = new WebPushClient();

            var payload = JsonSerializer.Serialize(new
            {
                title = title,
                message = message
            });

            var brokenSubs = new List<SavedSubscription>();

            foreach (var sub in userSubs)
            {
                try
                {
                    var pushSubscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails);
                    Console.WriteLine($"[WebPush] Successfully sent notification to user {userId} endpoint: {sub.Endpoint}");
                }
                catch (WebPushException ex)
                {
                    Console.WriteLine($"[WebPush] Failed to send push. Status code: {ex.StatusCode}. Message: {ex.Message}");
                    // If the subscription is expired or invalid, mark it for removal (410 Gone or 404 Not Found)
                    if (ex.StatusCode == System.Net.HttpStatusCode.Gone || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        brokenSubs.Add(sub);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WebPush] General error sending push: {ex.Message}");
                }
            }

            // Cleanup expired subscriptions
            if (brokenSubs.Any())
            {
                await _lock.WaitAsync();
                try
                {
                    if (File.Exists(_subsFilePath))
                    {
                        var json = await File.ReadAllTextAsync(_subsFilePath);
                        var currentSubs = JsonSerializer.Deserialize<List<SavedSubscription>>(json) ?? new();
                        currentSubs.RemoveAll(s => brokenSubs.Any(b => b.UserId == s.UserId && b.Endpoint == s.Endpoint));
                        await File.WriteAllTextAsync(_subsFilePath, JsonSerializer.Serialize(currentSubs, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
        }
    }
}
