using System;
using Firebase;
using Firebase.Auth;
using PushStars.UI;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    public static class SettingsAuthRegression
    {
        [MenuItem("Tools/Push Stars/Validate Settings Authentication")]
        public static void Run()
        {
            Require(SettingsAuthDialog.ValidateEmail("", "secret123", true, false) != null, "Empty email accepted.");
            Require(SettingsAuthDialog.ValidateEmail("invalid", "secret123", true, false) != null, "Malformed email accepted.");
            Require(SettingsAuthDialog.ValidateEmail("Player <player@example.com>", "secret123", true, false) != null, "Display-name address accepted.");
            Require(SettingsAuthDialog.ValidateEmail("player@example.com", "", false, false) != null, "Empty password accepted.");
            Require(SettingsAuthDialog.ValidateEmail("player@example.com", "123", true, false) != null, "Weak registration accepted.");
            Require(SettingsAuthDialog.ValidateEmail("player@example.com", "123", false, false) == null, "Existing account password incorrectly constrained.");
            Require(SettingsAuthDialog.ValidateEmail("player@example.com", "", false, true) == null, "Reset unexpectedly requires password.");
            Require(SettingsAuthDialog.ValidateEmail("player@example.com", "secret123", true, false) == null, "Valid registration rejected.");
            Require(SettingsAuthDialog.ErrorMessage(new OperationCanceledException()).Contains("cancelled"), "Cancellation shown as failure.");
            var collision = new FirebaseException((int)AuthError.CredentialAlreadyInUse, "private SDK payload");
            string message = SettingsAuthDialog.ErrorMessage(new AggregateException(collision));
            Require(message.Contains("EXISTING ACCOUNT"), "Link collision does not explain explicit account switching.");
            Require(!message.Contains("private SDK payload"), "SDK payload exposed.");
            Require(!SettingsAuthDialog.ErrorMessage(new Exception("secret-token")).Contains("secret-token"), "Unknown exception leaks data.");
            Debug.Log("[SettingsAuth] PASS: input validation, account collision, cancellation and safe error messages.");
        }

        private static void Require(bool valid, string message)
        { if (!valid) throw new Exception(message); }
    }
}
