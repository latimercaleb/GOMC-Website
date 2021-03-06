﻿using System;
using System.Data.Entity.Migrations;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Http;
using System.Web.WebPages;
using Project.Core;
using Project.Data;
using Project.Latex;
using Project.LoginSystem;
using Project.Models;
using Project.Models.LoginSystem;

namespace Project.Controllers
{
	public class AdminController : ApiController
	{
		private static readonly log4net.ILog log =
			log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);


		private const string ApplicationOctetStream = "application/octet-stream";

		public class DownloadLatexFileInput
		{
			public enum FileRequestKind
			{
				Pdf,
				HtmlZip
			}

			public int LatexUploadId { get; set; }
			public FileRequestKind Kind { get; set; }
		}

		public class FetchLatexUploadsOutput
		{
			public ValidateSessionResultType AuthResult { get; set; }
			public int Length => Uploads?.Length ?? 0;
			public LatexUploadItem[] Uploads { get; set; }
		}

		public class LatexUploadItem
		{
			public int Id { get; set; }
			public string Version { get; set; }
			public DateTime Created { get; set; }
		}

		public Func<ProjectDbContext> DbGetter { get; }

		public AdminController() : this(null)
		{
		}

		public AdminController(Func<ProjectDbContext> dbGetter)
		{
			DbGetter = dbGetter ?? (() => new ProjectDbContext());
		}

		[HttpPost]
		public FetchLatexUploadsOutput FetchLatexUploads()
		{
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return new FetchLatexUploadsOutput { AuthResult = authentication.Result };
			}

			using (var db = DbGetter())
			{
				var uploads = db.LatexUploads
					.OrderByDescending(j => j.Created)
					.Select(j => new LatexUploadItem
					{
						Id = j.Id,
						Version = j.Version,
						Created = j.Created
					}).ToArray();

				return new FetchLatexUploadsOutput
				{
					AuthResult = ValidateSessionResultType.SessionValid,
					Uploads = uploads
				};
			}
		}

		[HttpGet]
		public HttpResponseMessage DownloadLatexFile(int latexId, string kind)
		{
			var input = new DownloadLatexFileInput
			{
				Kind = Utils.EnumParse<DownloadLatexFileInput.FileRequestKind>(kind),
				LatexUploadId = latexId
			};
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return Request.CreateResponse(HttpStatusCode.Unauthorized);
			}

			byte[] fileBytes = null;

			using (var db = DbGetter())
			{
				var sqprm = new SqlParameter("@inputId", input.LatexUploadId);
				var up = db.LatexUploads.SqlQuery("SELECT * FROM dbo.LatexUploads WHERE Id = @inputId", sqprm).SingleOrDefault();
				if (up == null)
				{
					log.Error($"LatexUpload with id '{input.LatexUploadId}' was not found in db");
					return Request.CreateResponse(HttpStatusCode.Unauthorized);
				}
				switch (input.Kind)
				{
					case DownloadLatexFileInput.FileRequestKind.Pdf:
						fileBytes = up.Pdf;
						break;
					case DownloadLatexFileInput.FileRequestKind.HtmlZip:
						fileBytes = up.HtmlZip;
						break;
				}
			}

			var result = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(fileBytes)
			};

			result.Content.Headers.ContentDisposition =
				new ContentDispositionHeaderValue("attachment")
				{
					FileName = "output.pdf"
				};

			result.Content.Headers.ContentType =
				new MediaTypeHeaderValue(ApplicationOctetStream);

			return result;
		}

		[HttpGet]
		public HttpResponseMessage DownloadLogFile()
		{
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return Request.CreateResponse(HttpStatusCode.Unauthorized);
			}

			var logFilePath = @"C:\www\GOMC-Dev\temp\gomc.website.log";
			string fileTxt;
			using (var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (var sr = new StreamReader(fs, Encoding.Default))
			{
				fileTxt = sr.ReadToEnd();
			}
			var fileBytes = Encoding.UTF8.GetBytes(fileTxt);

			var result = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(fileBytes)
			};

			result.Content.Headers.ContentDisposition =
				new ContentDispositionHeaderValue("attachment")
				{
					FileName = "gomc.website.log"
				};

			result.Content.Headers.ContentType =
				new MediaTypeHeaderValue(ApplicationOctetStream);

			return result;
		}
		public class PublishLatexUploadOutput
		{
			public ValidateSessionResultType AuthResult { get; set; }

			public bool Success { get; set; }
		}
		[HttpGet]
		public PublishLatexUploadOutput PublishLatexUpload(int latexId)
		{
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return new PublishLatexUploadOutput
				{
					AuthResult = authentication.Result,
					Success = false
				};
			}

			var r = LatexController.PublishLatex(latexId);

			return new PublishLatexUploadOutput
			{
				AuthResult = authentication.Result,
				Success = r == LatexController.PublishLatexResult.Success
			};
		}
		public class FetchRegisteredUsersInput : FetchAnnouncementsInput
		{
			public string FilterName { get; set; }
			public string FilterEmail { get; set; }
			public bool IsDesc { get; set; }
			public RegisteredUsersOrderBy OrderBy { get; set; }
		}

		public enum RegisteredUsersOrderBy
		{
			Name,
			Email,
			Text,
			Created
		}

		[HttpPost]
		public FetchRegisteredUsersOutput FetchRegisteredUsers(FetchRegisteredUsersInput input)
		{
			Regex
				nameRegex = null,
				emailRegex = null;

			if (!input.FilterName.IsEmpty())
			{
				try
				{
					nameRegex = new Regex(input.FilterName);
				}
				catch
				{
					log.Error("Invalid name regex: " + input.FilterName);
				}
			}

			if (!input.FilterEmail.IsEmpty())
			{
				try
				{
					emailRegex = new Regex(input.FilterEmail);
				}
				catch
				{
					log.Error("Invalid email regex: " + input.FilterEmail);
				}
			}

			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return new FetchRegisteredUsersOutput { AuthResult = (authentication.Result) };
			}

			using (var db = DbGetter())
			{
				var allRegistrations = 
					(
						input.IsDesc
							? db.Registrations.OrderByDescending(orderByFn)
							: db.Registrations.OrderBy(orderByFn)
					)
					.Where(j => nameRegex == null || nameRegex.IsMatch(j.Name))
					.Where(j => emailRegex == null || emailRegex.IsMatch(j.Email))
					.ToArray();

				var totalLength = allRegistrations.Length;

				if (input.PageLength < 0)
				{
					input.PageLength = totalLength;
					input.PageIndex = 0;
				}

				var skip = input.PageLength * input.PageIndex;
				var take = input.PageLength;

				string orderByFn(RegistrationModel m)
				{
					switch (input.OrderBy)
					{
						case RegisteredUsersOrderBy.Name: return m.Name;
						case RegisteredUsersOrderBy.Email: return m.Email;
						case RegisteredUsersOrderBy.Text: return m.Text;
						case RegisteredUsersOrderBy.Created: return m.Created.ToString("G");
						default: return m.Name;
					}
				}

				var registrations = allRegistrations
					.Skip(skip)
					.Take(take).ToArray();

				return new FetchRegisteredUsersOutput
				{
					AuthResult = ValidateSessionResultType.SessionValid,
					Users = registrations,
					TotalLength = totalLength
				};
			}
		}

		public class FetchRegisteredUsersOutput
		{
			public ValidateSessionResultType AuthResult { get; set; }
			public int Length => Users?.Length ?? 0;
			public RegistrationModel[] Users { get; set; }
			public int TotalLength { get; set; }
		}

		[HttpGet]
		public HttpResponseMessage ExportRegisteredUsers(bool isDesc, RegisteredUsersOrderBy orderBy, string nameFilter, string emailFilter)
		{
			var reg = FetchRegisteredUsers(new FetchRegisteredUsersInput
			{
				PageIndex = 0,
				PageLength = -1,
				IsDesc = isDesc,
				OrderBy = orderBy,
				FilterName = nameFilter,
				FilterEmail = emailFilter
			});

			if (reg.AuthResult != ValidateSessionResultType.SessionValid)
			{
				return Request.CreateResponse(HttpStatusCode.Unauthorized);
			}

			byte[] fileBytes;

			var sr = new StringWriter();
			sr.WriteLine("Name, Email, Affiliation, Comment, Created");

			// escape commas, quotes and new lines
			string escapeStr(string str)
			{
				if (str == "") return " ";
				return str
					.Replace("\"", "'")
					.Replace("\n", "\\n")
					.Replace("\r", "\\r")
					.Replace(",", ";");
			}

			foreach (var r in reg.Users)
			{
				var str = $"{escapeStr(r.Name)},{escapeStr(r.Email)},{escapeStr(r.Affiliation)},{escapeStr(r.Text)},{r.Created:G}";

				sr.WriteLine(str);
			}

			fileBytes = sr.Encoding.GetBytes(sr.ToString());

			var result = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(fileBytes)
			};

			result.Content.Headers.ContentDisposition =
				new ContentDispositionHeaderValue("attachment")
				{
					FileName = "export.csv"
				};

			result.Content.Headers.ContentType =
				new MediaTypeHeaderValue(ApplicationOctetStream);

			return result;
		}

		[HttpPost]
		public AnnouncementResult NewAnnouncement(NewAnnouncementModel model)
		{
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return SessionToAnnouncementResult(authentication.Result);
			}

			using (var db = DbGetter())
			{
				var sqlParameter = new SqlParameter("@SessionInput", authentication.Session);

				var l = db.Database
					.SqlQuery<LoginSessions>("dbo.GetLoginIdFromSession @SessionInput", sqlParameter)
					.SingleOrDefault();

				if (l == null)
				{
					return AnnouncementResult.InvalidSession;
				}

				if (l.Expiration < DateTime.Now)
				{
					return AnnouncementResult.SessionExpired;
				}

				if (string.IsNullOrEmpty(model?.Content))
				{
					return AnnouncementResult.MissingContent;
				}

				var announcement = new AnnouncementModel
				{
					AuthorId = l.LoginId,
					Content = model.Content,
					Created = DateTime.Now
				};

				db.Announcements.Add(announcement);
				db.SaveChanges();

				return AnnouncementResult.Success;

			}
		}

		[HttpPost]
		public FetchAnnouncementsOutput GetAnnouncementsCount()
		{
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return new FetchAnnouncementsOutput { Result = SessionToAnnouncementResult(authentication.Result) };
			}
			using (var db = DbGetter())
			{
				var totalLength = db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.Announcements").Single();

				return new FetchAnnouncementsOutput
				{
					Result = AnnouncementResult.Success,
					TotalLength = totalLength
				};
			}
		}

		[HttpPost]
		public FetchAnnouncementsOutput FetchAnnouncements(FetchAnnouncementsInput input)
		{
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return new FetchAnnouncementsOutput { Result = SessionToAnnouncementResult(authentication.Result) };
			}

			using (var db = DbGetter())
			{
				var totalLength = db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.Announcements").Single();
				var skip = input.PageLength * input.PageIndex;
				var take = input.PageLength;

				var sqlQuery = "SELECT * FROM Announcements " +
							   "ORDER BY Created DESC " +
							   $"OFFSET ({skip}) ROWS FETCH NEXT ({take}) ROWS ONLY";

				var announcementResults = db.Announcements.SqlQuery(
					sqlQuery).ToArray();

				return new FetchAnnouncementsOutput
				{
					Result = AnnouncementResult.Success,
					Announcements = announcementResults,
					TotalLength = totalLength
				};
			}
		}

		[HttpPost]
		public DeleteAnnouncementOutput DeleteAnnouncement(DeleteAnnouncementInput input)
		{
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return new DeleteAnnouncementOutput { Result = SessionToAnnouncementResult(authentication.Result) };
			}

			using (var db = DbGetter())
			{
				const string query = "DELETE FROM dbo.Announcements " +
									 "WHERE Id = @inputAnnouncementId";

				var parm = new SqlParameter("@inputAnnouncementId", input.AnnouncementId);

				var affectedRows = db.Database.ExecuteSqlCommand(query, parm);

				return new DeleteAnnouncementOutput
				{
					Result = AnnouncementResult.Success,
					Deleted = affectedRows == 1
				};
			}
		}

		[HttpPost]
		public AnnouncementResult EditAnnouncement(EditAnnouncementInput input)
		{
			var authentication = Authenticate();

			if (authentication.Result != ValidateSessionResultType.SessionValid || !authentication.Session.HasValue)
			{
				Debug.Assert(authentication.Result != ValidateSessionResultType.SessionValid);
				return SessionToAnnouncementResult(authentication.Result);
			}

			using (var db = DbGetter())
			{
				const string query = "UPDATE dbo.Announcements " +
									 "SET Content = @inputContent " +
									 "WHERE Id = @inputAnnouncementId;";

				var parm1 = new SqlParameter("@inputAnnouncementId", input.AnnouncementId);
				var parm2 = new SqlParameter("@inputContent", input.NewContent);

				var affectedRows = db.Database.ExecuteSqlCommand(query, parm1, parm2);

				return affectedRows == 1 ? AnnouncementResult.Success : AnnouncementResult.MissingContent;
			}
		}

		public class DeleteAnnouncementOutput
		{
			public AnnouncementResult Result { get; set; }
			public bool Deleted { get; set; }
		}

		public class DeleteAnnouncementInput
		{
			public int AnnouncementId { get; set; }
		}

		public class EditAnnouncementInput
		{
			public int AnnouncementId { get; set; }
			public string NewContent { get; set; }
		}

		public class NewAnnouncementModel
		{
			public string Content { get; set; }
		}

		private class AuthenticateOutput
		{
			public ValidateSessionResultType Result { get; set; }
			public Guid? Session { get; set; }
		}

		private AuthenticateOutput Authenticate()
		{
			var sessionCookie = Request.GetCookie("Admin_Session_Guid");

			if (sessionCookie == null)
			{
				return new AuthenticateOutput { Result = ValidateSessionResultType.SessionInvalid, Session = null };
			}
			Guid session;

			if (!Guid.TryParse(sessionCookie, out session))
			{
				return new AuthenticateOutput { Result = ValidateSessionResultType.SessionInvalid, Session = session };
			}

			var validateSessionResultType = LoginManager.ValidateSession(session);

			switch (validateSessionResultType)
			{
				case ValidateSessionResultType.SessionExpired:
					return new AuthenticateOutput { Result = ValidateSessionResultType.SessionExpired, Session = session };

				case ValidateSessionResultType.SessionInvalid:
					return new AuthenticateOutput { Result = ValidateSessionResultType.SessionInvalid, Session = session };
			}

			var loginId = LoginManager.LoginIdFromSession(session);

			if (loginId == null)
			{
				return new AuthenticateOutput { Result = ValidateSessionResultType.SessionInvalid, Session = session };
			}

			return new AuthenticateOutput { Result = ValidateSessionResultType.SessionValid, Session = session };
		}

		public class FetchAnnouncementsInput
		{
			public int PageIndex { get; set; }
			public int PageLength { get; set; }
		}

		public class FetchAnnouncementsOutput
		{
			public AnnouncementResult Result { get; set; }
			public int Length => Announcements?.Length ?? 0;
			public AnnouncementModel[] Announcements { get; set; }
			public int TotalLength { get; set; }
		}

		private AnnouncementResult SessionToAnnouncementResult(ValidateSessionResultType result)
		{
			switch (result)
			{
				case ValidateSessionResultType.SessionValid:
					return AnnouncementResult.Success;

				case ValidateSessionResultType.SessionExpired:
					return AnnouncementResult.SessionExpired;

				case ValidateSessionResultType.SessionInvalid:
					return AnnouncementResult.InvalidSession;

				default:
					throw new ArgumentOutOfRangeException(nameof(result), result, null);
			}
		}

		public enum AnnouncementResult
		{
			Success,
			SessionExpired,
			InvalidSession,
			MissingContent
		}

	}
}