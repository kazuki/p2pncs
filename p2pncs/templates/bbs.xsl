<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:output method="xml" encoding="utf-8" doctype-public="-//W3C//DTD XHTML 1.1//EN" doctype-system="http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd" />

	<xsl:template match="/">
		<html>
			<head>
				<title>
					<xsl:text>p2pncs::bbs::</xsl:text>
					<xsl:value-of select="/page/bbs/title" />
				</title>
				<link type="text/css" rel="stylesheet" href="/css/ui-lightness/jquery-ui-1.7.2.custom.css" />
				<script type="text/javascript" charset="utf-8" src="/js/jquery-1.3.2.min.js" />
				<script type="text/javascript" charset="utf-8" src="/js/jquery-ui-1.7.2.custom.min.js" />
				<script type="text/javascript" charset="utf-8" src="/js/jquery-simple-dom.js" />
				<script type="text/javascript" charset="utf-8" src="/js/bbs.js" />
				<link type="text/css" rel="stylesheet" href="/css/bbs.css" />
			</head>
			<body>
				<h1><xsl:value-of select="/page/bbs/title" /></h1>
				<div id="bbsinfo">
					<div><xsl:value-of select="/page/bbs/@key" /></div>
					<div><xsl:value-of select="/page/bbs/@recordset" /></div>
				</div>
				<div id="bbsBody">
					<xsl:for-each select="/page/bbs/record">
						<xsl:sort select="posted" order="ascending" />
						<div>
							<div class="postName">
								<xsl:text>[</xsl:text>
								<xsl:value-of select="position()"/>
								<xsl:text>] </xsl:text>
								<xsl:value-of select="name" />
								<xsl:text>: </xsl:text>
								<xsl:value-of select="posted" />
							</div>
							<div class="postBody">
								<xsl:value-of select="body" />
							</div>
						</div>
					</xsl:for-each>
				</div>
				<hr />
				<div id="postForm">
					<xsl:element name="input">
						<xsl:attribute name="id">postKey</xsl:attribute>
						<xsl:attribute name="type">hidden</xsl:attribute>
						<xsl:attribute name="value">
							<xsl:value-of select="/page/bbs/@key"/>
						</xsl:attribute>
					</xsl:element>
					<div>
						<xsl:text>名前: </xsl:text>
						<input type="text" id="postName" />
						<button id="postButton">投稿</button>
						<input type="hidden" id="postToken" value="" />
						<input type="hidden" id="postAnswer" value="" />
						<input type="hidden" id="postPrev" value="" />
					</div>
					<div>
						<textarea cols="50" rows="8" id="postBody"></textarea>
					</div>
				</div>
			</body>
		</html>
	</xsl:template>
</xsl:stylesheet>
