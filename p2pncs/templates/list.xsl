<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">一覧 :: p2pncs</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/list.css" />
	</xsl:template>

	<xsl:template match="/page">
		<h1>一覧</h1>
		<table cellpadding="5" cellspacing="1" border="0">
			<thead>
				<tr>
					<td>タイトル</td>
					<td>タイプ</td>
					<td>ID</td>
					<td>ログ数</td>
					<td>最終更新日時</td>
					<td>最終管理更新日時</td>
					<td>作成日時</td>
				</tr>
			</thead>
			<xsl:for-each select="/page/file">
				<tr>
					<td>
						<xsl:element name="a">
							<xsl:attribute name="href">
								<xsl:value-of select="@key" />
							</xsl:attribute>
							<xsl:attribute name="target">_blank</xsl:attribute>
							<xsl:value-of select="title" />
						</xsl:element>
					</td>
					<td>
						<xsl:choose>
							<xsl:when test="@type = 'wiki'">
								<xsl:text>Wiki</xsl:text>
							</xsl:when>
							<xsl:when test="@type = 'simple-bbs'">
								<xsl:text>BBS</xsl:text>
							</xsl:when>
							<xsl:otherwise>
								<xsl:value-of select="@type" />
							</xsl:otherwise>
						</xsl:choose>
					</td>
					<td><xsl:value-of select="@key" /></td>
					<td><xsl:value-of select="@records" /></td>
					<td><xsl:value-of select="@lastModified" /></td>
					<td><xsl:value-of select="@lastManaged" /></td>
					<td><xsl:value-of select="@created" /></td>
				</tr>
			</xsl:for-each>
		</table>
	</xsl:template>
</xsl:stylesheet>
