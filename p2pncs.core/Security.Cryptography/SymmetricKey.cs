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
using openCrypto;

namespace p2pncs.Security.Cryptography
{
	public class SymmetricKey
	{
		SymmetricAlgorithmType _type;
		SymmetricAlgorithmPlus _algo;
		byte[] _iv;
		byte[] _key;
		bool _ivShuffle = true;
		static byte[] EmptyByteArray = new byte[0];
		public static readonly SymmetricKey NoneKey = new SymmetricKey (SymmetricAlgorithmType.None, null, null);

		#region Constructor
		public SymmetricKey (SymmetricAlgorithmType type, byte[] iv, byte[] key)
			: this (type, iv, key, CipherModePlus.CBC, PaddingMode.ISO10126, true)
		{
		}
		public SymmetricKey (SymmetricAlgorithmPlus algo, byte[] iv, byte[] key, bool enableIVShuffle)
		{
			if (algo == null)
				_type = SymmetricAlgorithmType.None;
			else if (algo is openCrypto.CamelliaManaged)
				_type = SymmetricAlgorithmType.Camellia;
			else if (algo is openCrypto.RijndaelManaged)
				_type = SymmetricAlgorithmType.Rijndael;
			else
				throw new NotSupportedException ();
			_algo = algo;
			_iv = iv;
			_key = key;
			_ivShuffle = enableIVShuffle;
		}
		public SymmetricKey (SymmetricAlgorithmType type, byte[] iv, byte[] key, CipherModePlus cipherMode, PaddingMode paddingMode, bool enableIVShuffle)
		{
			_type = type;
			switch (type) {
				case SymmetricAlgorithmType.None:
					_algo = null;
					return;
				case SymmetricAlgorithmType.Camellia:
					_algo = new openCrypto.CamelliaManaged ();
					break;
				case SymmetricAlgorithmType.Rijndael:
					_algo = new openCrypto.RijndaelManaged ();
					break;
				default:
					throw new ArgumentOutOfRangeException ();
			}

			_algo.ModePlus = cipherMode;
			_algo.Padding = paddingMode;
			_algo.KeySize = key.Length << 3;
			_algo.BlockSize = iv.Length << 3;
			_algo.FeedbackSize = iv.Length << 3;
			_iv = iv;
			_key = key;
			_ivShuffle = enableIVShuffle;
		}
		#endregion

		#region Serialize
		public byte[] GetSerializedByteArray ()
		{
			if (_key == null)
				return new byte[] { (byte)_type };
			byte[] raw = new byte[2 + _key.Length + _iv.Length];
			raw[0] = (byte)_type;
			raw[1] = (byte)_key.Length;
			_key.CopyTo (raw, 2);
			_iv.CopyTo (raw, _key.Length + 2);
			return raw;
		}
		public static SymmetricKey CreateFromSerializedByteArray (byte[] raw)
		{
			SymmetricAlgorithmType type;
			byte[] key = null, iv = null;

			type = (SymmetricAlgorithmType)raw[0];
			if (raw.Length > 1) {
				key = new byte[raw[1]];
				iv = new byte[raw.Length - key.Length - 2];
				if (key.Length + iv.Length + 2 != raw.Length)
					throw new FormatException ();
				for (int i = 0; i < key.Length; i++)
					key[i] = raw[i + 2];
				for (int i = 0; i < iv.Length; i++)
					iv[i] = raw[i + key.Length + 2];
			}
			return new SymmetricKey (type, iv, key);
		}
		#endregion

		#region Properties
		public SymmetricAlgorithmType AlgorithmType {
			get { return _type; }
		}

		public byte[] Key {
			get { return _key; }
		}

		public byte[] IV {
			get { return _iv; }
		}

		public bool EnableIVShuffle {
			get { return _ivShuffle; }
		}

		public PaddingMode Padding {
			get { return _algo == null ? PaddingMode.None : _algo.Padding; }
		}
		#endregion

		#region Methods
		public byte[] Encrypt (byte[] input, int inputOffset, int inputCount)
		{
			if (_type == SymmetricAlgorithmType.None) {
				byte[] output = new byte[inputCount];
				Buffer.BlockCopy (input, inputOffset, output, 0, inputCount);
				return output;
			}

			using (ICryptoTransform ct = CreateEncryptor ()) {
				int diff = inputCount % _iv.Length;
				int shuffleSize = (_ivShuffle ? _iv.Length : 0);
				int outputCount = (diff == 0 ? inputCount + shuffleSize : (inputCount / _iv.Length) * _iv.Length + shuffleSize);
				if (_algo.Padding != PaddingMode.None)
					outputCount += _iv.Length;
				byte[] output = new byte[outputCount];
				if (_ivShuffle)
					ct.TransformBlock (RNG.GetBytes (_iv.Length), 0, _iv.Length, output, 0);
				for (int i = 0; i <= inputCount - _iv.Length; i += _iv.Length)
					ct.TransformBlock (input, inputOffset + i, _iv.Length, output, shuffleSize + i);
				if (diff == 0) {
					byte[] tail = ct.TransformFinalBlock (EmptyByteArray, 0, 0);
					Buffer.BlockCopy (tail, 0, output, inputCount + shuffleSize, tail.Length);
				} else {
					byte[] tail = ct.TransformFinalBlock (input, inputCount - diff, diff);
					Buffer.BlockCopy (tail, 0, output, inputCount + shuffleSize - diff, tail.Length);
				}
				return output;
			}
		}

		public byte[] Decrypt (byte[] input, int inputOffset, int inputCount)
		{
			if (_type == SymmetricAlgorithmType.None) {
				byte[] output = new byte[inputCount];
				Buffer.BlockCopy (input, inputOffset, output, 0, inputCount);
				return output;
			}

			using (ICryptoTransform ct = CreateDecryptor ()) {
				byte[] temp = new byte[inputCount];
				if (_ivShuffle) {
					ct.TransformBlock (input, inputOffset, _iv.Length, temp, 0);
					inputOffset += _iv.Length;
					inputCount -= _iv.Length;
				}
				int pos = 0;
				if (inputCount > _iv.Length) {
					ct.TransformBlock (input, inputOffset, inputCount - _iv.Length, temp, 0);
					pos += inputCount - _iv.Length;
				}
				byte[] tail = ct.TransformFinalBlock (input, inputOffset + pos, _iv.Length);
				Buffer.BlockCopy (tail, 0, temp, pos, tail.Length);
				Array.Resize<byte> (ref temp, pos + tail.Length);
				return temp;
			}
		}

		public ICryptoTransform CreateEncryptor ()
		{
			if (_algo == null)
				return DummyCryptoTransformer.Instance;
			return _algo.CreateEncryptor (_key, _iv);
		}

		public ICryptoTransform CreateDecryptor ()
		{
			if (_algo == null)
				return DummyCryptoTransformer.Instance;
			return _algo.CreateDecryptor (_key, _iv);
		}
		#endregion

		#region Internal Class
		class DummyCryptoTransformer : ICryptoTransform
		{
			static DummyCryptoTransformer _instance = new DummyCryptoTransformer ();
			DummyCryptoTransformer () { }

			public static ICryptoTransform Instance {
				get { return _instance; }
			}

			public bool CanReuseTransform {
				get { return true; }
			}

			public bool CanTransformMultipleBlocks {
				get { return true; }
			}

			public int InputBlockSize {
				get { return 1; }
			}

			public int OutputBlockSize {
				get { return 1; }
			}

			public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
			{
				Buffer.BlockCopy (inputBuffer, inputOffset, outputBuffer, outputOffset, inputCount);
				return inputCount;
			}

			public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
			{
				byte[] buf = new byte[inputCount];
				Buffer.BlockCopy (inputBuffer, inputOffset, buf, 0, inputCount);
				return buf;
			}

			public void Dispose ()
			{
			}
		}
		#endregion
	}
}
