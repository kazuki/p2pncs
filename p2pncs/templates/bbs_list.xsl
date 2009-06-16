<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:output method="xml" encoding="utf-8" doctype-public="-//W3C//DTD XHTML 1.1//EN" doctype-system="http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd" />

	<xsl:template match="/">
		<html>
			<head>
				<title>p2pncs::bbs</title>
				<link type="text/css" rel="stylesheet" href="/css/bbs_list.css" />
			</head>
			<body>
				<h1>List of BBS</h1>
				<table cellpadding="5" cellspacing="1" border="0">
					<tr class="header">
						<td rowspan="2">Title</td>
						<td>ID</td>
					</tr>
					<tr class="header">
						<td>Current Cache ID</td>
					</tr>
					<xsl:for-each select="/page/bbs">
						<tr>
							<td rowspan="2">
								<xsl:element name="a">
									<xsl:attribute name="href">
										<xsl:text>/bbs/</xsl:text>
										<xsl:value-of select="@key" />
									</xsl:attribute>
									<xsl:value-of select="title" />
								</xsl:element>
							</td>
							<td><xsl:value-of select="@key" /></td>
						</tr>
						<tr>
							<td><xsl:value-of select="@recordset" /></td>
						</tr>
					</xsl:for-each>
				</table>
			</body>
		</html>
	</xsl:template>
</xsl:stylesheet>
