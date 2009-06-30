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
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using openCrypto.EllipticCurve.Signature;

namespace p2pncs.Security.Captcha
{
	public class SimpleCaptcha : ICaptchaAuthority
	{
		ECDSA _ecdsa;
		byte[] _hmac_key;
		byte[] _salt;
		const string _chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
		const int _len = 10;
		Font _font;
		Size _size;
		Brush _brush = new SolidBrush (Color.Black);

		public SimpleCaptcha (ECDSA ecdsa)
		{
			_ecdsa = ecdsa;
			_hmac_key = openCrypto.RNG.GetRNGBytes (64);
			_salt = openCrypto.RNG.GetRNGBytes (32);

			_font = new Font (FontFamily.GenericMonospace, 28, FontStyle.Bold);
			using (Image img = new Bitmap (16, 16, PixelFormat.Format24bppRgb))
			using (Graphics g = Graphics.FromImage (img)) {
				g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
				SizeF size = g.MeasureString (new string ('Z', _len), _font);
				_size = new Size ((int)(size.Width + 10), (int)(size.Height + 10));
			}
		}

		public CaptchaChallengeData GetChallenge (byte[] hash)
		{
			Random rnd = new Random (BitConverter.ToInt32 (openCrypto.RNG.GetRNGBytes (4), 0));
			string txt = "";
			for (int i = 0; i < _len; i++)
				txt += _chars[rnd.Next (0, _chars.Length)].ToString ();

			byte[] data;
			using (Image img = new Bitmap (_size.Width, _size.Height, PixelFormat.Format24bppRgb))
			using (Graphics g = Graphics.FromImage (img)) {
				g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
				SizeF size = g.MeasureString (txt, _font);
				g.Clear (Color.White);
				g.DrawString (txt, _font, _brush, new PointF ((_size.Width - size.Width) / 2.0F, (_size.Height - size.Height) / 2.0f));

				using (MemoryStream ms = new MemoryStream ()) {
					img.Save (ms, ImageFormat.Png);
					ms.Close ();
					data = ms.ToArray ();
				}
			}

			return new CaptchaChallengeData (0, ComputeToken (hash, Encoding.ASCII.GetBytes (txt)), data);
		}

		public byte[] Verify (byte[] hash, byte[] token, byte[] answer)
		{
			byte[] token2 = ComputeToken (hash, answer);
			if (token.Length != token2.Length)
				return null;
			for (int i = 0; i < token.Length; i ++)
				if (token[i] != token2[i])
					return null;
			return _ecdsa.SignHash (hash);
		}

		byte[] ComputeToken (byte[] hash, byte[] ascii_text)
		{
			using (HMAC hmac = new HMACSHA1 (_hmac_key, true)) {
				hmac.Initialize ();
				hmac.TransformBlock (hash, 0, hash.Length, null, 0);
				hmac.TransformBlock (ascii_text, 0, ascii_text.Length, null, 0);
				hmac.TransformFinalBlock (_salt, 0, _salt.Length);
				return hmac.Hash;
			}
		}
	}
}
