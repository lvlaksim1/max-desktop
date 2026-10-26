import { CaptchaWidget, CaptchaPublicEvents } from "@vkid/captcha";

// Keep VK's event bridge encapsulated. The local WPF WebView2 page receives
// only the token returned by the user's actual completed captcha.
window.MaxDesktopCaptchaSdk = { CaptchaWidget, CaptchaPublicEvents };
