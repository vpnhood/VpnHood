# Login setup for test environment

To sign in in the debug and development environment, read this general info:
https://developers.google.com/android/guides/client-auth#windows

## Steps for Visual Studio
1. To get the debug certificate fingerprint for Visual Studio:
https://learn.microsoft.com/en-us/previous-versions/xamarin/android/platform/maps-and-location/maps/obtaining-a-google-maps-api-key?tabs=windows#obtaining-your-signing-key-fingerprint

Run the following command in the terminal and get the SHA-1 (Do not use SHA-256):
* Replace `<user>` with your Windows username.
* The default password for the debug keystore is `android`.
```
keytool -list -v -alias androiddebugkey -keystore "C:\Users\<user>\AppData\Local\Xamarin\Mono for Android\debug.keystore"
```

2. Go to Firebase > Project > Settings > General > Your apps > SHA certificate fingerprints and add the SHA-1 fingerprints obtained in the previous step.
3. Download the `google-services.json` file from Firebase and place it in the `Properties` directory of the project.
4. Add your account as a tester in Google Console at: Google Play Console > Settings > License Testing > License testers
