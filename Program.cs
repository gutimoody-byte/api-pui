using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PuiWebhookApi.Controllers;
using PuiWebhookApi.Workers;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configuración JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]))
        };
    });

var secretKey = builder.Configuration["Jwt:SecretKey"];
var issuer = builder.Configuration["Jwt:Issuer"];
var audience = builder.Configuration["Jwt:Audience"];

var tokenPrueba = JwtHelper.GenerarToken(secretKey, issuer, audience);
Console.WriteLine($"Token de prueba: {tokenPrueba}");

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// Connection string desde appsettings.json
string connectionString = builder.Configuration.GetConnectionString("bdPrueba");

// Registrar repositorio como Singleton
builder.Services.AddSingleton(new CoincidenciaRepository(connectionString));

builder.Services.AddHostedService<CoincidenciaWorker>();
builder.Services.AddHttpClient<PuiAuthService>();

var app = builder.Build();

// Middleware para cabeceras de seguridad
app.Use(async (context, next) =>
{
    // Anti-Clickjacking
    context.Response.Headers.Add("X-Frame-Options", "DENY");

    // Content Security Policy (ejemplo básico)
    context.Response.Headers.Add("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'");

    // Otras cabeceras recomendadas
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("Referrer-Policy", "no-referrer");

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

