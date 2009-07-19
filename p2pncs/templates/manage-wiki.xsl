<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="2.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">
		<xsl:value-of select="/page/file/title" />
		<xsl:text> :: 管理 :: p2pncs</xsl:text>
	</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/manage-wiki.css" />
	</xsl:template>

	<xsl:template match="/page">
		<xsl:element name="form">
			<xsl:attribute name="method">post</xsl:attribute>
			<xsl:attribute name="action">
				<xsl:text>/manage/</xsl:text>
				<xsl:value-of select="file/@key" />
			</xsl:attribute>

			<div>
				<h1>ヘッダ</h1>
				<table>
					<tr>
						<td>タイトル: </td>
						<td>
							<xsl:element name="input">
								<xsl:attribute name="type">text</xsl:attribute>
								<xsl:attribute name="size">80</xsl:attribute>
								<xsl:attribute name="name">title</xsl:attribute>
								<xsl:attribute name="value">
									<xsl:value-of select="/page/file/title" />
								</xsl:attribute>
							</xsl:element>
						</td>
					</tr>
					<tr>
						<td style="vertical-align: top; padding-top: 0.5em;">認証サーバ: </td>
						<td>
							<textarea name="auth" cols="80" rows="6">
								<xsl:for-each select="/page/file/auth-servers/auth-server">
									<xsl:value-of select="concat(serialize, '&#13;&#10;')" />
								</xsl:for-each>
							</textarea>
						</td>
					</tr>
				</table>
			</div>

			<div>
				<h1>レコード</h1>
				<xsl:for-each select="/page/file/records/record">
					<xsl:sort select="@created" order="ascending" />

					<div>
						<div class="postName">
							<xsl:element name="input">
								<xsl:attribute name="type">checkbox</xsl:attribute>
								<xsl:attribute name="checked">checked</xsl:attribute>
								<xsl:attribute name="name">record</xsl:attribute>
								<xsl:attribute name="value">
									<xsl:value-of select="@hash" />
								</xsl:attribute>
							</xsl:element>
							<xsl:text>[</xsl:text>
							<xsl:value-of select="wiki/title"/>
							<xsl:text>] </xsl:text>
							<xsl:text>: </xsl:text>
							<xsl:value-of select="@created" />
							<xsl:text> (</xsl:text>
							<xsl:value-of select="wiki/name" />
							<xsl:text>)</xsl:text>
						</div>
					</div>
				</xsl:for-each>
			</div>

			<p>
				<xsl:text>管理更新を実行すると、チェックの付いたレコードのみを残して、チェックのないレコードを削除します。</xsl:text>
				<br/>
				<xsl:text>また、管理更新によって加えられた変更を元の状態に戻すことは出来ません。</xsl:text>
			</p>

			<p>
				<input type="submit" value="管理更新を実行" />
			</p>

		</xsl:element>
		
	</xsl:template>
</xsl:stylesheet>
