using Microsoft.EntityFrameworkCore;
using System.Text;
using TripPlanner.Api.Common;
using TripPlanner.Api.Data;
using TripPlanner.Api.Features.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Backend.Services.Crypto;
using Backend.Services.Storage;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

// PostgreSQL
builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

// JWT options
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt configuration missing.");
builder.Services.AddSingleton(jwt);

// Auth feature
builder.Services.AddScoped<AuthModule>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(15)
        };

        opt.Events = new JwtBearerEvents
        {
        OnAuthenticationFailed = ctx =>
        {
            Console.WriteLine("JWT ERROR: " + ctx.Exception.Message);
            return Task.CompletedTask;
        }
        };
    });

builder.Services.AddAuthorization();


//Crypto
builder.Services.AddScoped<IJoinPasswordCryptoService, JoinPasswordCryptoService>();

var app = builder.Build();

//app.UseSwagger();
//app.UseSwaggerUI();

app.UseHttpsRedirection();

// ✅ DomainException -> 400, ForbiddenException -> 403, unexpected -> 500
app.UseMiddleware<ExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
