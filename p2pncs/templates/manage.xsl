<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">管理 :: p2pncs</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/bbs_list.css" />
	</xsl:template>

	<xsl:template match="/page">
		<h1>管理権限を持っているファイルの一覧</h1>
		<table cellpadding="5" cellspacing="1" border="0">
			<tr class="header">
				<td>Title</td>
				<td>Type</td>
				<td>ID</td>
			</tr>
			<xsl:for-each select="/page/file">
				<tr>
					<td>
						<xsl:element name="a">
							<xsl:attribute name="href">
								<xsl:text>/manage/</xsl:text>
								<xsl:value-of select="@key" />
							</xsl:attribute>
							<xsl:choose>
								<xsl:when test="@type='simple-bbs'">
									<xsl:value-of select="bbs/title" />
								</xsl:when>
								<xsl:when test="@type='wiki'">
									<xsl:value-of select="wiki/title" />
								</xsl:when>
							</xsl:choose>
						</xsl:element>
					</td>
					<td>
						<xsl:value-of select="@type" />
					</td>
					<td>
						<xsl:value-of select="@key" />
					</td>
				</tr>
			</xsl:for-each>
		</table>
	</xsl:template>
</xsl:stylesheet>
