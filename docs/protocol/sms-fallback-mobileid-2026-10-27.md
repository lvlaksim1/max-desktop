# MAX Desktop: SMS fallback when Mobile ID has no cellular network — 2026-10-27

## Goal
The owner wants: authorize own accounts in MAX Desktop -> open @verificationcodes_bot -> «Посмотреть код» -> view genuine code on Windows. Verification must be authorized by MAX itself, without fabricating operator responses.

## Authoritative server prerequisites
- MAX official help confirms a second method of binding a trusted device for «Коды подтверждения»: SMS when an account has **пароль для входа (2FA)**. Sequence «Посмотреть код» -> «Привязать новое устройство» -> SMS. https://help.max.ru/help/gosuslugi/svyazka_gosuslug_s_max/kak_smenit_doverennoe_ustroistvo_dlya_vhoda_v_gosuslugi
- 2FA password is created in the mobile MAX application's Settings / Security, **not web MAX**: https://help.max.ru/help/account/zashshita_prophilya/kak_ustanovit_ili_otklyuchit_parol_dlya_vhoda_v_max
- This help page concerns **Gosuslugi** trusted-device management. Whether the exact SMS alternative applies to every code type, account, and PC is **not established**.
- MAX Desktop already supports SMS verification for *sign-in* and a second-factor password `AUTH_CHECK_PASSWORD`. The SMS challenge for *trusted-device binding* is a separate server-managed procedure; do not conflate them.

## Evidence from exact Android 26.35.1 (6867) APK
The original JavaScript interfaces are registered in `pil.java`: `WebViewHandler` public and `PrivateWebViewHandler` for eligible mini-apps. Their methods are `postEvent(String eventName, String json)`, implemented in `jql.java` and `koe.java`. Source: https://github.com/lvlaksim1/max-android-research/blob/main/docs/webapp-jsbridge-inspection-26.35.1.md

The public `https://st.max.ru/js/max-web-app.js` checks for `window.WebViewHandler`, dispatches via `postEvent` and has `window.WebApp.sendEvent(name,json)`. Research: https://github.com/lvlaksim1/max-android-research/blob/main/docs/public-max-bridge-js-sms-2026-10-27.md

Original native `WebAppVerifyMobileId` accepts `requestId, url`. Android's `qrk.java` branch 17 emits `gj9(new jj9("no_cellular", 1))` when an Android cellular network is missing. `qi4.java` serializes `gu6` error with `requestId` and `error.code=client.verify_mobile_id.no_cellular`, then returns private `WebAppVerifyMobileId` event. Full trace: https://github.com/lvlaksim1/max-android-research/blob/main/docs/mobileid-bridge-trace-26.35.1.md

## MAX Desktop v0.1.22 experimental improvement
- `src/MaxDesktop.Protocol/MaxMiniAppCellularUnavailable.cs`: pure validated, JSON-escaped negative response with code `client.verify_mobile_id.no_cellular`. Rejects empty, overlong or control-character request IDs.
- `src/MaxDesktop.App/MiniAppWindow.cs`: injects a minimal `WebViewHandler.postEvent` and `PrivateWebViewHandler.postEvent` in the top document **only on the HTTPS origin signed by MAX**. Private bridge accepts only `WebAppVerifyMobileId`. It reads **only** the operation's requestId, discards the URL, and sends `no_cellular` to mini-app `window.WebApp.sendEvent` using the public bridge API.
- On Windows there is no Android `TRANSPORT_CELLULAR`; returning this negative result is correct and does not manufacture Mobile ID success, manipulate user identity or request SMS.
- All other private events remain unsupported; no host objects, permissions, credentials, URLs or secret responses are exposed. System VPN not modified; no login/session data changed.
- Smoke tests validate the error envelope and identifier input guards.

## How to assess
On a dedicated test account with **2FA configured in real Android MAX**, run v0.1.22, sign in, open last Gosuslugi item in @verificationcodes_bot, press «Посмотреть код» -> «Привязать новое устройство». If page invokes Mobile ID over private bridge, our client replies immediately `no_cellular`. The **server or mini-app** may then offer a genuine SMS challenge. Only enter codes into the official mini-app's own form.

**Unknowns**: whether the specific mini-app currently uses the public `WebApp.sendEvent` dispatcher for its private result; whether the signed entry URL and final web document have the same origin; whether the fallback is offered for desktop-class devices; whether 2FA state is enabled for the tested account; whether fallback is limited to Gosuslugi rather than bank/operator codes. These require actual acceptance; no ability to view protected codes is claimed as proven.

If SMS still does not appear, do not add a fake manual SMS dialog. The next stage requires determining the **real server-issued SMS challenge** rather than guessing an endpoint or weakening the trust check.
