# The SDK picks this file up by its name. Optimized, never renamed, as in CLIENT's website build: no
# store's obfuscation rule covers a website APK, renaming would save little, and the SDK's own switch
# (AndroidR8ObfuscationMode=disabled) would turn the optimizing off as well.
-dontobfuscate
