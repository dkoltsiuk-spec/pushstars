package com.pushstars.auth;

import android.app.Activity;
import androidx.annotation.Keep;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import com.unity3d.player.UnityPlayer;
import org.json.JSONObject;

@Keep
public final class GoogleSignInBridge {
    public static void signIn(String receiver, String requestId) {
        Activity activity = UnityPlayer.currentActivity;
        activity.runOnUiThread(() -> {
            try {
                int clientResource = activity.getResources().getIdentifier(
                    "default_web_client_id", "string", activity.getPackageName());
                if (clientResource == 0) {
                    reply(receiver, requestId, null, "Google sign-in is unavailable in this build.", false);
                    return;
                }
                GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(
                    activity.getString(clientResource)).build();
                GetCredentialRequest request = new GetCredentialRequest.Builder()
                    .addCredentialOption(option).build();
                CredentialManager.create(activity).getCredentialAsync(activity, request, null,
                    command -> activity.runOnUiThread(command),
                    new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                        @Override public void onResult(GetCredentialResponse response) {
                            try {
                                GoogleIdTokenCredential credential = GoogleIdTokenCredential.createFrom(
                                    response.getCredential().getData());
                                reply(receiver, requestId, credential.getIdToken(), null, false);
                            } catch (Exception e) {
                                reply(receiver, requestId, null, "Google returned an invalid sign-in response.", false);
                            }
                        }
                        @Override public void onError(GetCredentialException error) {
                            reply(receiver, requestId, null, "Google sign-in failed. Please try again.",
                                error instanceof GetCredentialCancellationException);
                        }
                    });
            } catch (Exception e) {
                reply(receiver, requestId, null, "Google sign-in could not start. Please try again.", false);
            }
        });
    }

    private static void reply(String receiver, String requestId, String token, String error, boolean cancelled) {
        try {
            JSONObject data = new JSONObject();
            data.put("requestId", requestId);
            data.put("idToken", token);
            data.put("error", error);
            data.put("cancelled", cancelled);
            UnityPlayer.UnitySendMessage(receiver, "OnNativeSignIn", data.toString());
        } catch (Exception ignored) { /* Only fixed strings and SDK token strings are serialized. */ }
    }
}
