<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:output method="xml" encoding="utf-8" doctype-public="-//W3C//DTD XHTML 1.1//EN" doctype-system="http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd" />

	<xsl:template match="/">
		<html>
			<head>
				<title>CAPTCHA認証サーバ</title>
			</head>
			<body>
				<h1 style="font-size: x-large">
					<xsl:text>p2pncs(RinGOch) </xsl:text>
					<xsl:value-of select="/page/@ver" />
				</h1>
				<h2 style="font-size: large">認証サーバ情報</h2>
				<div style="border: 1px solid #666; padding: 1em; background-color: #ffc">
					<xsl:value-of select="/page/authinfo" />
				</div>
			</body>
		</html>
	</xsl:template>
</xsl:stylesheet>
