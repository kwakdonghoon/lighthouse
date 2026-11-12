// iOS 전용 네이티브 도우미 (실기기 미확인). 공유 시트와 "동작 줄이기" 설정.
#import <UIKit/UIKit.h>

extern UIViewController *UnityGetGLViewController(void);

extern "C" {
    void LhShareText(const char *text, const char *name) {
        NSString *body = text ? [NSString stringWithUTF8String:text] : @"";
        NSString *subject = name ? [NSString stringWithUTF8String:name] : @"";
        dispatch_async(dispatch_get_main_queue(), ^{
            UIViewController *root = UnityGetGLViewController();
            if (root == nil) return;
            UIActivityViewController *vc = [[UIActivityViewController alloc] initWithActivityItems:@[body] applicationActivities:nil];
            [vc setValue:subject forKey:@"subject"];
            if (vc.popoverPresentationController != nil) {        // iPad 는 기준 위치가 있어야 한다
                vc.popoverPresentationController.sourceView = root.view;
                vc.popoverPresentationController.sourceRect = CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height / 2, 1, 1);
                vc.popoverPresentationController.permittedArrowDirections = (UIPopoverArrowDirection)0;
            }
            [root presentViewController:vc animated:YES completion:nil];
        });
    }

    int LhIsReduceMotion(void) {
        return UIAccessibilityIsReduceMotionEnabled() ? 1 : 0;
    }
}
