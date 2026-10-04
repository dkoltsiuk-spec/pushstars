#import <Foundation/Foundation.h>
#include <string.h>

// Only the user's device-region preference; no location services or permission request.
extern "C" const char* _pushStarsCountryCode(void)
{
    static char code[3] = {0, 0, 0};
    @autoreleasepool {
        NSString* region = [[NSLocale currentLocale] objectForKey:NSLocaleCountryCode];
        const char* value = [[region uppercaseString] UTF8String];
        code[0] = code[1] = code[2] = 0;
        if (value != NULL && strlen(value) == 2) { code[0] = value[0]; code[1] = value[1]; }
    }
    return code;
}
