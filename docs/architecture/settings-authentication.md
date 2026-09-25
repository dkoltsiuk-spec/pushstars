# Settings authentication

The Apple, Google and Email buttons open `SettingsAuthDialog` on the existing Settings overlay. No scene regeneration is needed.

- Email supports sign-in, registration and password reset. Registration links the current Firebase user, preserving its UID and cloud profile.
- **Save current progress** links Apple/Google credentials to the current UID. An account collision stays an error; it never silently switches accounts.
- **Sign in** explicitly selects the provider account and reloads Main when its UID changes, so profile views rebind. It does not merge guest cloud progress or upload the app's separate local PlayerPrefs progress.
- Settings initializes Firebase on demand, sharing initialization with Boot. Later launches restore the Firebase session in the background. Passwords and provider tokens are never saved by the app or printed to logs.
- Apple/Google use native UI on mobile; Editor uses Email. Apple on Android uses Firebase's federated provider flow. Google Android uses Credential Manager; iOS uses GoogleSignIn. Apple iOS uses AuthenticationServices with random nonce and state validation.

## Firebase configuration and remaining device setup

The Firebase project is `push-stars-d620e`, bundle/package `com.pushstars.app`. On 2026-09-24, enabled Email/Password, Google and Apple through the project's Beast Core account and confirmed each provider is Enabled. Anonymous remains enabled. Set the Google public app name to Push Stars and selected the existing project owner's support address. Downloaded updated iOS and Android configuration files into Assets; these remain gitignored. The iOS plist now includes `CLIENT_ID` and `REVERSED_CLIENT_ID`. The existing local iOS provisioning profile already contains `com.apple.developer.applesignin`.

1. Keep Anonymous enabled (guest upgrade depends on it).
2. For Android Google sign-in, register SHA-1/SHA-256 for the actual signing certificates and download `google-services.json` again. Firebase currently has no fingerprints; this workstation has no configured Android release keystore or default debug keystore. The Android config must generate `default_web_client_id`. Update CI's Firebase plist secret with the downloaded iOS configuration as well; it can replace the local plist during export.
3. The Xcode export hook adds the Apple capability/entitlement and AuthenticationServices framework. Use a provisioning profile with that capability. For Android Apple sign-in, the Apple Services ID, Team ID, key and Firebase return URL still need configuration in Firebase/Apple Developer; the native iOS flow does not require those web-flow fields.
4. Resolve Android dependencies with External Dependency Manager. iOS export resolves GoogleSignIn via CocoaPods or the supported Swift Package resolver and registers the reversed client URL scheme.

Reference: [Firebase Google authentication](https://firebase.google.com/docs/auth/unity/google-signin), [Firebase Apple authentication](https://firebase.google.com/docs/auth/unity/apple), [Android Credential Manager](https://developer.android.com/identity/sign-in/credential-manager-siwg-implementation), [Google iOS integration](https://developers.google.com/identity/sign-in/ios/sign-in).

## Validation

Verified locally: Unity C# compilation; Android and iOS conditional C# compilation; iOS export hook compilation; Play Mode button wiring, registration/sign-in toggle, invalid email rejection, password masking/clearing, dialog close/reopen and visual layout at 1080×1920. `PushStars.Editor.SettingsAuthRegression.Run` repeats the input/error regression checks without a network request.

Still required on configured devices: native iOS/Android build; new guest → link (UID unchanged); existing account sign-in (correct cloud profile); collision (guest retained); cancellation; offline/retry; reset email delivery; restart/session restore; account deletion after reauthentication. Native SDK builds and live Firebase authentication were not run on this Windows workstation.
