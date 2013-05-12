﻿using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Nemcache.Service
{
    class StaticFileHttpHandler : HttpHandlerBase
    {
        public override async Task Get(HttpListenerContext httpContext, params string[] matches)
        {
            var bytes = File.ReadAllBytes("test.html");
            httpContext.Response.ContentType = "text/html";
            httpContext.Response.StatusCode = 200;
            await httpContext.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            httpContext.Response.Close();
        }
    }
}