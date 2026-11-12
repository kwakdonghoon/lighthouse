// iOS 전용 사진 공유 도우미 (실기기 미확인). 사진 모드에서 저장한 PNG 를 공유 시트(이미지 저장 포함)로 보인다. 어디로도 자동 전송하지 않는다.
#import <UIKit/UIKit.h>

extern UIViewController *UnityGetGLViewController(void);

extern "C" {
    void LhSharePhoto(const char *path) {
        if (path == NULL) return;
        NSString *p = [NSString stringWithUTF8String:path];
        dispatch_async(dispatch_get_main_queue(), ^{
            UIViewController *root = UnityGetGLViewController();
            UIImage *img = [UIImage imageWithContentsOfFile:p];
            if (root == nil || img == nil) return;
            UIActivityViewController *vc = [[UIActivityViewController alloc] initWithActivityItems:@[img] applicationActivities:nil];
            if (vc.popoverPresentationController != nil) {        // iPad 는 기준 위치가 있어야 한다
                vc.popoverPresentationController.sourceView = root.view;
                vc.popoverPresentationController.sourceRect = CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height / 2, 1, 1);
                vc.popoverPresentationController.permittedArrowDirections = (UIPopoverArrowDirection)0;
            }
            [root presentViewController:vc animated:YES completion:nil];
        });
    }
}
