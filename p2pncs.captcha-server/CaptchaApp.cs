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
using System.Security.Cryptography;
using System.Xml;
using Kazuki.Net.HttpServer;
using Kazuki.Net.HttpServer.TemplateEngines;
using openCrypto.EllipticCurve;
using p2pncs.Net.Overlay;
using p2pncs.Net.Overlay.DFS.MMLC;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;
using SysNet = System.Net;

namespace p2pncs.captcha_server
{
	public class CaptchaApp : IHttpApplication
	{
		ICaptchaAuthority _captcha;
		ECKeyPair _privateKey;
		XslTemplateEngine _template = new XslTemplateEngine ();
		string _template_file, _authinfo;
		const int MAX_REQUEST_BODY = 1048576; // 1MB

		public CaptchaApp (ICaptchaAuthority captcha, ECKeyPair privateKey, string host, ushort port, string template_file)
		{
			_captcha = captcha;
			_privateKey = privateKey;
			_template_file = template_file;

			SysNet.EndPoint ep;
			try {
				SysNet.IPAddress adrs = SysNet.IPAddress.Parse (host);
				ep = new SysNet.IPEndPoint (adrs, port);
			} catch {
				ep = new AuthServerInfo.DnsEndPoint (host, port);
			}
			_authinfo = new AuthServerInfo (new Key (_captcha.PublicKey), ep).ToParsableString ();
		}

		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			if (req.HttpMethod == HttpMethod.GET) {
				return Process_TopPage (server, req, res);
			} else {
				return Process_Captcha (server, req, res);
			}
		}

		object Process_TopPage (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			XmlDocument doc = new XmlDocument ();
			doc.AppendChild (doc.CreateElement ("page"));
			doc.DocumentElement.SetAttribute ("ver", System.Reflection.Assembly.GetCallingAssembly ().GetName ().Version.ToString ());
			
			XmlElement publicKey = doc.CreateElement ("public-key");
			publicKey.AppendChild (doc.CreateTextNode (Convert.ToBase64String (_captcha.PublicKey)));
			doc.DocumentElement.AppendChild (publicKey);

			XmlElement authinfo = doc.CreateElement ("authinfo");
			authinfo.AppendChild (doc.CreateTextNode (_authinfo));
			doc.DocumentElement.AppendChild (authinfo);

			return _template.Render (server, req, res, doc, _template_file);
		}

		object Process_Captcha (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			if (!req.HasContentBody ())
				throw new HttpException (HttpStatusCode.BadRequest);
			byte[] body = req.GetContentBody (MAX_REQUEST_BODY);
			object obj_req = CaptchaContainer.Decrypt (_privateKey, body);
			object response = null;
			byte[] req_iv, req_key;
			if (obj_req is CaptchaChallengeRequest) {
				CaptchaChallengeRequest creq = (CaptchaChallengeRequest)obj_req;
				req_iv = creq.IV;
				req_key = creq.Key;
				response = _captcha.GetChallenge (creq.Hash);
				CaptchaChallengeData data = _captcha.GetChallenge (creq.Hash);
			} else if (obj_req is CaptchaAnswer) {
				CaptchaAnswer ans = (CaptchaAnswer)obj_req;
				req_iv = ans.IV;
				req_key = ans.Key;
				response = new CaptchaVerifyResult (_captcha.Verify (ans.Hash, ans.Token, ans.Answer));
			} else {
				throw new HttpException (HttpStatusCode.BadRequest);
			}

			byte[] plain = Serializer.Instance.Serialize (response);
			SymmetricKey key = new SymmetricKey (SymmetricAlgorithmType.Camellia, req_iv, req_key, openCrypto.CipherModePlus.CTR, PaddingMode.ISO10126, false);
			byte[] encrypted = key.Encrypt (plain, 0, plain.Length);
			res[HttpHeaderNames.ContentType] = "application/octet-stream";
			res[HttpHeaderNames.Connection] = "close";
			res[HttpHeaderNames.ContentLength] = encrypted.Length.ToString ();
			return encrypted;
		}
	}
}
