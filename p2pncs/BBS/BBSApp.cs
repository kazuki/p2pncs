/*
 * Copyright (C) 2009 Kazuki Oikawa
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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using p2pncs.BBS;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;

namespace p2pncs.BBS
{
	class BBSWebApp : WebApp.IMergeableFileCommonProcess
	{
		static BBSWebApp _instance = new BBSWebApp ();
		public static BBSWebApp Instance {
			get { return _instance; }
		}
		BBSWebApp () {}

		#region IMergeableFileCommonProcess Members

		void ParseNewPagePostData (Dictionary<string, string> dic, out string fpname, out string fpbody)
		{
			fpname = Helpers.GetValueSafe (dic, "fpname").Trim ();
			fpbody = Helpers.GetValueSafe (dic, "fpbody").Trim ();
		}

		public bool ParseNewPagePostData (Dictionary<string, string> dic, out IHashComputable header, out IHashComputable[] records)
		{
			string fpname, fpbody;
			ParseNewPagePostData (dic, out fpname, out fpbody);

			header = new SimpleBBSHeader ();
			if (fpbody.Length == 0) {
				records = null;
			} else {
				records = new IHashComputable[] {
					new SimpleBBSRecord (fpname, fpbody)
				};
			}
			return true;
		}

		public void OutputNewPageData (Dictionary<string, string> dic, XmlElement validationRoot)
		{
			XmlDocument doc = validationRoot.OwnerDocument;
			string fpname, fpbody;
			ParseNewPagePostData (dic, out fpname, out fpbody);

			validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "fpname" } }, new[] {
				doc.CreateElement ("value", null, new[]{doc.CreateTextNode (fpname)})
			}));
			validationRoot.AppendChild (doc.CreateElement ("data", new string[][] { new[] { "name", "fpbody" } }, new[] {
				doc.CreateElement ("value", null, new[]{doc.CreateTextNode (fpbody)})
			}));
		}

		public string NewPageXSL {
			get { return "bbs_new.xsl"; }
		}

		public IHashComputable ParseNewPostData (Dictionary<string, string> dic)
		{
			string name = Helpers.GetValueSafe (dic, "name").Trim ();
			string body = Helpers.GetValueSafe (dic, "body").Trim ();
			if (body.Length == 0)
				throw new ArgumentException ("本文には文字を入力する必要があります");
			return new SimpleBBSRecord (name, body);
		}

		#endregion

		public object ProcessGetRequest (Node node, IHttpRequest req, HttpResponseHeader res, MergeableFileHeader header, string tail_url)
		{
			List<MergeableFileRecord> records = node.MMLC.GetRecords (header.Key, out header);
			if (records == null)
				throw new HttpException (HttpStatusCode.NotFound);

			using (ISessionTransaction transaction = req.Session.BeginTransaction (System.Data.IsolationLevel.Serializable)) {
				string session_state_key = header.Key.ToBase64String () + "/read";
				Key[] keys = transaction.ReadState (session_state_key) as Key[];
				HashSet<Key> readSet = (keys == null ? new HashSet<Key> () : new HashSet<Key> (keys));
				List<Key> currentSet = new List<Key> (records.Count);
				for (int i = 0; i < records.Count; i ++) {
					SimpleBBSRecord content = records[i].Content as SimpleBBSRecord;
					if (content == null) continue;
					content.IsNew = readSet.Add (records[i].Hash);
					currentSet.Add (records[i].Hash);
				}
				readSet.IntersectWith (currentSet);
				keys = new Key[readSet.Count];
				readSet.CopyTo (keys);
				transaction.UpdateState (session_state_key, keys);
				transaction.Commit ();
			}

			XmlDocument doc = XmlHelper.CreateEmptyDocument ();
			doc.DocumentElement.AppendChild (XmlHelper.CreateMergeableFileElement (doc, header, records.ToArray ()));
			return WebApp.Template.Render (req, res, doc, Path.Combine (WebApp.DefaultTemplatePath, "bbs_view.xsl"));
		}
	}
}