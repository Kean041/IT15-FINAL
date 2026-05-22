FinSight is an ERP system for SMEs to manage budgets and forecasting.

## Secret configuration

Do not store production secrets in `appsettings.json` or source control.

For local development, use ASP.NET Core User Secrets:

```powershell
dotnet user-secrets set "AlphaVantage:ApiKey" "<value>"
dotnet user-secrets set "TwoFactor:OTPSecretKey" "<value>"
dotnet user-secrets set "Email:SenderPassword" "<value>"
dotnet user-secrets set "Stripe:SecretKey" "<value>"
dotnet user-secrets set "Stripe:PublishableKey" "<value>"
dotnet user-secrets set "Stripe:WebhookSecret" "<value>"
dotnet user-secrets set "Cloudflare:TurnstileSiteKey" "<value>"
dotnet user-secrets set "Cloudflare:TurnstileSecretKey" "<value>"
dotnet user-secrets set "SeedUsers:SuperAdminPassword" "<value>"
```

For deployment, configure these environment variables in the hosting provider:

```text
ConnectionStrings__DefaultConnection
AlphaVantage__ApiKey
TwoFactor__OTPSecretKey
Email__SenderPassword
Stripe__SecretKey
Stripe__PublishableKey
Stripe__WebhookSecret
Cloudflare__TurnstileSiteKey
Cloudflare__TurnstileSecretKey
SeedUsers__SuperAdminPassword
```

