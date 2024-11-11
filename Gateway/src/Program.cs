/*
 * AufBauWerk Erweiterungen für Vivendi
 * Copyright (C) 2024  Manuel Meitinger
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using AufBauWerk.Vivendi.Gateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
Settings settings = new();
builder.Configuration
    .GetRequiredSection("Gateway")
    .Bind(settings, options => options.ErrorOnUnknownConfiguration = true);
builder.Services
    .AddWindowsService(options => options.ServiceName = "VivendiGateway")
    .AddSingleton(settings)
    .AddSingleton<CertificateAuthority>()
    .AddControllers();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(settings.BuildJwtOptions());
WebApplication app = builder.Build();
app.UseAuthentication();
app.MapControllers();
app.Run();
