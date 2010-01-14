/*
 * Copyright (C) 2009-2010 Kazuki Oikawa
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
using System.IO;
using Kazuki.Net.HttpServer;
using openCrypto.EllipticCurve;
using openCrypto.EllipticCurve.Signature;
using p2pncs.Security.Captcha;
using p2pncs.Security.Cryptography;
using XmlConfigLibrary;

namespace p2pncs.captcha_server
{
	class Program
	{
		const string CONFIG_PATH = "p2pncs.captcha-server.xml";
		const string CONFIG_BIND_PORT = "captcha-server/bind-port";
		const string CONFIG_PRIVATE_KEY = "captcha-server/private-key";
		const string CONFIG_HOST = "captcha-server/host";

		static void Main (string[] args)
		{
			XmlConfig config = new XmlConfig ();
			ECKeyPair privateKey;
			LoadConfig (config, out privateKey);
			SimpleCaptcha captcha = new SimpleCaptcha (new ECDSA (privateKey), 4);
			CaptchaApp app = new CaptchaApp (captcha, privateKey, config.GetValue<string> (CONFIG_HOST),
				(ushort)config.GetValue<int> (CONFIG_BIND_PORT), "templates/captcha-server.xsl");
			using (IHttpServer server = HttpServer.CreateEmbedHttpServer (app, null, true, false, true, config.GetValue<int> (CONFIG_BIND_PORT), 64)) {
				Console.WriteLine ("Captcha Authentication Server is Running...");
				Console.WriteLine ("Press enter key to exit");
				Console.ReadLine ();
			}
		}

		static void LoadConfig (XmlConfig config, out ECKeyPair privateKey)
		{
			bool saveFlag = false;

			config.Define<int> (CONFIG_BIND_PORT, IntParser.Instance, new IntRangeValidator (1, ushort.MaxValue), 8080);
			config.Define<byte[]> (CONFIG_PRIVATE_KEY, BinaryParser.Instance, null, null);
			config.Define<string> (CONFIG_HOST, StringParser.Instance, null, string.Empty);

			try {
				if (File.Exists (CONFIG_PATH))
					config.Load (CONFIG_PATH);
			} catch {
				saveFlag = true;
			}

			byte[] raw = config.GetValue<byte[]> (CONFIG_PRIVATE_KEY);
			while (true) {
				if (raw == null || raw.Length == 0) {
					privateKey = ECKeyPair.Create (DefaultAlgorithm.ECDomainName);
					config.SetValue<byte[]> (CONFIG_PRIVATE_KEY, privateKey.PrivateKey, false);
					saveFlag = true;
				} else {
					privateKey = ECKeyPairExtensions.CreatePrivate (raw);
					if (privateKey.DomainName != DefaultAlgorithm.ECDomainName) {
						raw = null;
						continue;
					}
				}
				break;
			}

			if (config.GetValue<string> (CONFIG_HOST).Length == 0) {
				config.SetValue<string> (CONFIG_HOST, "localhost", false);
				saveFlag = true;
			}

			if (saveFlag)
				config.Save (CONFIG_PATH);
		}
	}
}
