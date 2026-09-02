// PushStarsHaptics — Taptic Engine feedback, called from PushStars.Core.Haptics.
//
// Two entry points: a selection tick (the bottom-nav tabs) and a discrete impact in three
// weights (0 light / 1 medium / 2 heavy). Generators are created once and kept for the app's
// lifetime — there is no release path, which is also what keeps this correct under both ARC
// and manual reference counting.

#import <UIKit/UIKit.h>

static UISelectionFeedbackGenerator *sSelection = nil;
static UIImpactFeedbackGenerator *sLight = nil;
static UIImpactFeedbackGenerator *sMedium = nil;
static UIImpactFeedbackGenerator *sHeavy = nil;

static void RunOnMain(void (^block)(void))
{
    if ([NSThread isMainThread]) {
        block();
    } else {
        dispatch_async(dispatch_get_main_queue(), block);
    }
}

static UIImpactFeedbackGenerator *ImpactGenerator(int style)
{
    switch (style) {
        case 0:
            if (sLight == nil) {
                sLight = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
            }
            return sLight;
        case 2:
            if (sHeavy == nil) {
                sHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
            }
            return sHeavy;
        default:
            if (sMedium == nil) {
                sMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            }
            return sMedium;
    }
}

extern "C" {

void _pushStarsHapticSelection(void)
{
    RunOnMain(^{
        if (sSelection == nil) {
            sSelection = [[UISelectionFeedbackGenerator alloc] init];
        }
        [sSelection prepare];
        [sSelection selectionChanged];
    });
}

void _pushStarsHapticImpact(int style)
{
    RunOnMain(^{
        UIImpactFeedbackGenerator *generator = ImpactGenerator(style);
        [generator prepare];
        [generator impactOccurred];
    });
}

}
