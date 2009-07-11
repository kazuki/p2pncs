<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">初期ノード :: p2pncs</xsl:template>
 
	<xsl:template match="/page">
		<h1>初期ノードに接続</h1>
		<xsl:if test="count(connected/endpoint) &gt; 0">
			<div style="margin-bottom: 1em">
				<xsl:text>以下の</xsl:text>
				<xsl:value-of select="count(connected/endpoint)" />
				<xsl:text>ノードへの接続を試行しました</xsl:text>
				<xsl:for-each select="connected/endpoint">
					<div>
						<xsl:value-of select="text()" />
					</div>
				</xsl:for-each>
			</div>
		</xsl:if>
		<form method="post" action="/net/init" accept-charset="UTF-8" >
			<div><textarea cols="30" rows="5" name="nodes" /></div>
			<div><input type="submit" value="接続" /></div>
		</form>

		<h1>初期ノード情報へ変換</h1>
		<form method="post" action="/net/init" accept-charset="UTF-8">
			<table>
				<tr>
					<td>IPアドレス/ドメイン名: </td>
					<td><input type="text" size="30" name="ip"/></td>
				</tr>
				<tr>
					<td>ポート番号: </td>
					<td><input type="text" size="30" name="port"/></td>
				</tr>
				<tr>
					<td colspan="2">
						<input type="submit" value="変換" />
					</td>
				</tr>
			</table>
		</form>
		<xsl:if test="encoded">
			<xsl:if test="encoded/error">
				<div>
					<xsl:value-of select="encoded/source" />
					<xsl:text>を認識できませんでした。</xsl:text>
				</div>
			</xsl:if>
			<xsl:if test="not(encoded/error)">
				<p>
					<xsl:value-of select="encoded/source" />
					<xsl:text>の初期ノード情報は以下の通りです。下記のテキストをコピーしてご利用ください。</xsl:text>
				</p>
				<div style="margin: 0.5em; padding: 0.5em; background-color: #ffc; border: 1px solid #000">
					<xsl:value-of select="encoded/text()" />
				</div>
			</xsl:if>
		</xsl:if>
		<xsl:if test="ipendpoint">
			<h1>自ノード情報</h1>
			<table>
				<tr>
					<td>IPアドレス: </td>
					<td><xsl:value-of select="ipendpoint/@ip" /></td>
				</tr>
				<tr>
					<td>ポート番号: </td>
					<td><xsl:value-of select="ipendpoint/@port" /></td>
				</tr>
				<tr>
					<td>初期ノード形式: </td>
					<td><xsl:value-of select="ipendpoint/text()" /></td>
				</tr>
			</table>
		</xsl:if>
	</xsl:template>
</xsl:stylesheet>
