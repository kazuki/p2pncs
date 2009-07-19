<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">一覧 :: BBS :: p2pncs</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/bbs_list.css" />
	</xsl:template>

	<xsl:template match="/page">
		<h1>一覧</h1>
		<table cellpadding="5" cellspacing="1" border="0">
			<tr class="header">
				<td>Title</td>
				<td>レス数</td>
				<td>最終書き込み日時</td>
				<td>ID</td>
				<td>作成日時</td>
				<td>最終管理更新日時</td>
			</tr>
			<xsl:for-each select="/page/file[@type='simple-bbs']">
				<tr>
					<td>
						<xsl:element name="a">
							<xsl:attribute name="href">
								<xsl:text>/bbs/</xsl:text>
								<xsl:value-of select="@key" />
							</xsl:attribute>
							<xsl:attribute name="target">_blank</xsl:attribute>
							<xsl:value-of select="title" />
						</xsl:element>
					</td>
					<td><xsl:value-of select="@records" /></td>
					<td><xsl:value-of select="@lastModified" /></td>
					<td><xsl:value-of select="@key" /></td>
					<td><xsl:value-of select="@created" /></td>
					<td><xsl:value-of select="@lastManaged" /></td>
				</tr>
			</xsl:for-each>
		</table>
	</xsl:template>
</xsl:stylesheet>
