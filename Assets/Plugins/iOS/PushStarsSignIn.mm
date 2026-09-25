#import <AuthenticationServices/AuthenticationServices.h>
#import <GoogleSignIn/GoogleSignIn.h>
#import "UnityAppController.h"
#import "UnityInterface.h"
#import "PluginBase/AppDelegateListener.h"

static void PSAuthReply(NSString *receiver, NSString *requestId, NSDictionary *fields) {
    NSMutableDictionary *reply = [fields mutableCopy];
    reply[@"requestId"] = requestId;
    NSData *data = [NSJSONSerialization dataWithJSONObject:reply options:0 error:nil];
    NSString *json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage(receiver.UTF8String, "OnNativeSignIn", json.UTF8String);
    });
}

@interface PSAuthBridge : NSObject <ASAuthorizationControllerDelegate,
    ASAuthorizationControllerPresentationContextProviding, AppDelegateListener>
@property(nonatomic, copy) NSString *receiver;
@property(nonatomic, copy) NSString *requestId;
@property(nonatomic, strong) ASAuthorizationController *controller;
@end

@implementation PSAuthBridge
- (void)onOpenURL:(NSNotification *)notification {
    NSURL *url = notification.userInfo[@"url"];
    if (url) [GIDSignIn.sharedInstance handleURL:url];
}
- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController *)controller {
    return UnityGetGLViewController().view.window;
}
- (void)authorizationController:(ASAuthorizationController *)controller
    didCompleteWithAuthorization:(ASAuthorization *)authorization {
    if (![authorization.credential isKindOfClass:ASAuthorizationAppleIDCredential.class]) {
        PSAuthReply(self.receiver, self.requestId, @{@"error": @"Apple returned an invalid sign-in response."});
        return;
    }
    ASAuthorizationAppleIDCredential *credential = authorization.credential;
    if (![credential.state isEqualToString:self.requestId]) {
        PSAuthReply(self.receiver, self.requestId, @{@"error": @"Apple sign-in could not be verified. Please retry."});
        return;
    }
    NSString *token = [[NSString alloc] initWithData:credential.identityToken encoding:NSUTF8StringEncoding];
    NSString *code = [[NSString alloc] initWithData:credential.authorizationCode encoding:NSUTF8StringEncoding];
    PSAuthReply(self.receiver, self.requestId, @{@"idToken": token ?: @"", @"authorizationCode": code ?: @""});
    self.controller = nil;
}
- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithError:(NSError *)error {
    PSAuthReply(self.receiver, self.requestId, @{
        @"cancelled": @(error.code == ASAuthorizationErrorCanceled),
        @"error": @"Apple sign-in failed. Please try again."
    });
    self.controller = nil;
}
@end

static PSAuthBridge *appleBridge;
static PSAuthBridge *urlListener;

extern "C" void PSAuthSignIn(const char *receiver, const char *requestId, const char *provider, const char *nonceHash) {
    NSString *target = [NSString stringWithUTF8String:receiver];
    NSString *request = [NSString stringWithUTF8String:requestId];
    NSString *providerId = [NSString stringWithUTF8String:provider];
    NSString *nonce = [NSString stringWithUTF8String:nonceHash];
    dispatch_async(dispatch_get_main_queue(), ^{
        if ([providerId isEqualToString:@"apple.com"]) {
            appleBridge = [PSAuthBridge new];
            appleBridge.receiver = target;
            appleBridge.requestId = request;
            ASAuthorizationAppleIDRequest *appleRequest = [[ASAuthorizationAppleIDProvider new] createRequest];
            appleRequest.requestedScopes = @[ASAuthorizationScopeEmail, ASAuthorizationScopeFullName];
            appleRequest.nonce = nonce;
            appleRequest.state = request;
            appleBridge.controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[appleRequest]];
            appleBridge.controller.delegate = appleBridge;
            appleBridge.controller.presentationContextProvider = appleBridge;
            [appleBridge.controller performRequests];
            return;
        }
        if (!urlListener) {
            urlListener = [PSAuthBridge new];
            UnityRegisterAppDelegateListener(urlListener);
        }
        NSString *path = [NSBundle.mainBundle pathForResource:@"GoogleService-Info" ofType:@"plist"];
        NSDictionary *config = path ? [NSDictionary dictionaryWithContentsOfFile:path] : nil;
        NSString *clientId = config[@"CLIENT_ID"];
        if (clientId.length == 0) {
            PSAuthReply(target, request, @{@"error": @"Google sign-in is unavailable in this build."});
            return;
        }
        GIDSignIn.sharedInstance.configuration = [[GIDConfiguration alloc] initWithClientID:clientId];
        [GIDSignIn.sharedInstance signInWithPresentingViewController:UnityGetGLViewController()
            completion:^(GIDSignInResult *result, NSError *error) {
                if (error) {
                    PSAuthReply(target, request, @{
                        @"cancelled": @(error.code == kGIDSignInErrorCodeCanceled),
                        @"error": @"Google sign-in failed. Please try again."
                    });
                    return;
                }
                PSAuthReply(target, request, @{
                    @"idToken": result.user.idToken.tokenString ?: @"",
                    @"accessToken": result.user.accessToken.tokenString ?: @""
                });
            }];
    });
}
