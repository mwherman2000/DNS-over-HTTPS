﻿/*
Technitium DNS-over-HTTPS
Copyright (C) 2019  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using TechnitiumLibrary.Net.Dns;
using TechnitiumLibrary.Net.Dns.ClientConnection;

namespace DNS_over_HTTPS.NETCore
{
    public class Startup
    {
        static NameServerAddress _dnsServer;

        public IConfiguration Configuration { get; set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            _dnsServer = new NameServerAddress(Configuration.GetValue<string>("DnsServer"));
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.Run(async (context) =>
            {
                await Task.Run(() =>
                {
                    HttpRequest Request = context.Request;
                    HttpResponse Response = context.Response;

                    if (Request.Path == "/dns-query")
                    {
                        string acceptTypes = Request.Headers["accept"];
                        if ((acceptTypes != null) && !acceptTypes.Contains("application/dns-message"))
                            throw new NotSupportedException("DoH request type not supported.");

                        try
                        {
                            DnsDatagram request;

                            switch (Request.Method)
                            {
                                case "GET":
                                    string strRequest = Request.Query["dns"];
                                    if (string.IsNullOrEmpty(strRequest))
                                        throw new ArgumentNullException("dns");

                                    //convert from base64url to base64
                                    strRequest = strRequest.Replace('-', '+');
                                    strRequest = strRequest.Replace('_', '/');

                                    //add padding
                                    int x = strRequest.Length % 4;
                                    if (x > 0)
                                        strRequest = strRequest.PadRight(strRequest.Length - x + 4, '=');

                                    request = new DnsDatagram(new MemoryStream(Convert.FromBase64String(strRequest)));
                                    break;

                                case "POST":
                                    if (Request.ContentType != "application/dns-message")
                                        throw new NotSupportedException("DNS request type not supported: " + Request.ContentType);

                                    request = new DnsDatagram(Request.Body);
                                    break;

                                default:
                                    throw new NotSupportedException("DoH request type not supported."); ;
                            }

                            DnsClientConnection connection = DnsClientConnection.GetConnection((DnsTransportProtocol)Enum.Parse(typeof(DnsTransportProtocol), Configuration.GetValue<string>("DnsServerProtocol"), true), _dnsServer, null);
                            connection.Timeout = Configuration.GetValue<int>("DnsTimeout");

                            ushort originalRequestId = request.Header.Identifier;

                            DnsDatagram response = connection.Query(request);
                            if (response == null)
                            {
                                Response.StatusCode = (int)HttpStatusCode.GatewayTimeout;
                                Response.WriteAsync("<p>DNS query timed out.</p>");
                            }
                            else
                            {
                                response.Header.SetIdentifier(originalRequestId); //set id since dns connection may change it if 2 clients have same id

                                Response.ContentType = "application/dns-message";

                                using (MemoryStream mS = new MemoryStream())
                                {
                                    response.WriteTo(mS);
                                    mS.WriteTo(Response.Body);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Response.Clear();
                            Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                            Response.WriteAsync("<h1>500 Internal Server Error</h1>");
                            Response.WriteAsync("<p>" + ex.ToString() + "</p>");
                        }
                    }
                    else
                    {
                        Response.StatusCode = (int)HttpStatusCode.NotFound;
                        Response.WriteAsync("<h1>404 Not Found</h1>");
                    }
                });
            });
        }
    }
}
