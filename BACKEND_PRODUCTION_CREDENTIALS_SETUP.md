# TripConnect Backend Production Credentials Setup

The backend is prepared so production setup only needs Azure/App Store/Google/Firebase/LiveKit credentials.

Put these in Azure App Service -> Environment variables.

## 1. Database

```text
ConnectionStrings__DefaultConnection
```

Value:

```text
Server=tcp:YOUR_SERVER.database.windows.net,1433;Initial Catalog=YOUR_DB;Persist Security Info=False;User ID=YOUR_USER;Password=YOUR_PASSWORD;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

## 2. JWT

```text
Jwt__Issuer
Jwt__Audience
Jwt__SigningKey
Jwt__AccessMinutes
Jwt__RefreshDays
```

Use:

- `Jwt__Issuer`: `TripPlanner`
- `Jwt__Audience`: `TripPlannerClients`
- `Jwt__SigningKey`: long random secret, at least 64 characters
- `Jwt__AccessMinutes`: `30`
- `Jwt__RefreshDays`: `14`

## 3. Azure Blob Storage

```text
AzureBlob__ConnectionString
AzureBlob__ContainerName
```

Example:

```text
AzureBlob__ContainerName=event-images
```

Images are resized before upload in `AzureBlobStorageService`.

## 4. Encryption

```text
Encryption__Key
```

Use a 32-byte base64 key.

PowerShell example:

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }))
```

## 5. Email / OTP / Forgot password

```text
Email__Host
Email__Port
Email__Username
Email__Password
Email__From
```

For Gmail use an App Password, not the normal Gmail password.

## 6. LiveKit

```text
LiveKit__ServerUrl
LiveKit__ApiKey
LiveKit__ApiSecret
LiveKit__TokenMinutes
```

Example:

```text
LiveKit__ServerUrl=wss://YOUR_PROJECT.livekit.cloud
LiveKit__TokenMinutes=30
```

## 7. Firebase server push

Firebase Console -> Project Settings -> Service Accounts -> Generate new private key.

Recommended Azure values:

```text
Firebase__ProjectId
Firebase__ServiceAccountBase64
```

PowerShell Base64 command:

```powershell
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content .\firebase-service-account.json -Raw)))
```

Alternative if Azure accepts raw multiline JSON:

```text
Firebase__ServiceAccountJson
```

## 8. Payments / store validation

The backend now supports config-driven store validation.

For development/testing without real store validation:

```text
Billing__RequireStoreValidation=false
Billing__PlusProductId=tripconnect_plus_monthly
```

For production:

```text
Billing__RequireStoreValidation=true
Billing__PlusProductId=tripconnect_plus_monthly
```

### Google Play validation

Google Play Console / Google Cloud:

1. Create service account.
2. Grant it access to the app in Play Console.
3. Download service account JSON.

Azure variables:

```text
Billing__GooglePlay__PackageName=com.myapp
Billing__GooglePlay__ServiceAccountBase64
```

PowerShell Base64 command:

```powershell
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content .\google-play-service-account.json -Raw)))
```

Alternative:

```text
Billing__GooglePlay__ServiceAccountJson
```

### Apple App Store validation

App Store Connect:

1. Create In-App Purchase subscription product:

```text
tripconnect_plus_monthly
```

2. Create App Store Connect API key with access to In-App Purchase / App Store Server API.
3. Download `.p8` private key.
4. Copy:
   - Issuer ID
   - Key ID
   - Bundle ID

Azure variables:

```text
Billing__Apple__BundleId=com.bahaa.fyp.app
Billing__Apple__IssuerId=YOUR_APP_STORE_CONNECT_ISSUER_ID
Billing__Apple__KeyId=YOUR_APP_STORE_CONNECT_KEY_ID
Billing__Apple__PrivateKeyBase64=YOUR_P8_FILE_AS_BASE64
Billing__Apple__UseSandbox=false
```

For TestFlight/sandbox testing:

```text
Billing__Apple__UseSandbox=true
```

PowerShell Base64 command:

```powershell
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content .\AuthKey_XXXXXXXXXX.p8 -Raw)))
```

Alternative:

```text
Billing__Apple__PrivateKeyPem
```

## 9. GitHub Actions deployment secret

In each backend GitHub repository, add one of these repository secrets:

Preferred:

```text
AZURE_WEBAPP_PUBLISH_PROFILE
```

Or existing fallback name:

```text
AZUREAPPSERVICE_PUBLISHPROFILE_4BD25096AF9A4E768C968F18C81C32D5
```

Value: full Azure publish profile XML from Azure App Service -> Download publish profile.

The workflow is already prepared to use either name.

## 10. After changing Azure variables

Restart the Azure Web App.

Then test:

```text
GET /api/test
POST /api/auth/login
GET /api/billing/plans
```
