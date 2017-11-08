﻿using System;
using System.IO;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using Project.Core;
using Project.Data;
using Project.Latex;
using Project.Models;
using Project.LoginSystem;

namespace Project.Controllers
{
	public class LatexController : ApiController
	{
		[HttpPost]
		public LatexConvertResult Convert()
		{
			var g = Request.GetCookie("Admin_Session_Guid");

			if (g == null)
			{
				return LatexConvertResult.BadSession;
			}
			Guid session;

			if (!Guid.TryParse(g, out session))
			{
				return LatexConvertResult.BadSession;
			}

			var r = LoginManager.ValidateSession(session);

			switch(r)
			{
				case ValidateSessionResultType.SessionExpired:
					return LatexConvertResult.SessionExpired;

				case ValidateSessionResultType.SessionInvalid:
					return LatexConvertResult.BadSession;
			}

			var loginId = LoginManager.LoginIdFromSession(session);

			if(loginId == null)
			{
				return LatexConvertResult.BadSession;
			}

			var root = HttpContext.Current.Server.MapPath("~/temp/uploads");
			Directory.CreateDirectory(root);
			var provider = new MultipartFormDataStreamProvider(root);
			var tsk = Request.Content.ReadAsMultipartAsync(provider);
			tsk.Wait();

			var version = provider.FormData["version"];

			if(version == null)
			{
				return LatexConvertResult.MissingVersion;
			}

			if(provider.FileData.Count == 0 || !File.Exists(provider.FileData[0].LocalFileName))
			{
				return LatexConvertResult.MissingFile;
			}

			var file = File.ReadAllText(provider.FileData[0].LocalFileName);

			using (var db = new ProjectDbContext())
			{
				var conv = new LatexConvertor();

				var convRes = conv.Convert(file);

				if (convRes != ConversionResult.Success)
				{
					return LatexConvertResult.InvalidFormat;
				}

				var model = new LatexUploadModel
				{
					AuthorId = loginId.Value,
					Version = version,
					HtmlZip = conv.HtmlZip,
					Pdf = conv.Pdf,
					Created = DateTime.Now
				};

				db.LatexUploads.Add(model);
				db.SaveChanges();
				return LatexConvertResult.Success;
			}
		}
	}
}