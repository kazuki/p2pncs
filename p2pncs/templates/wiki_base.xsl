<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:output method="xml" encoding="utf-8" doctype-public="-//W3C//DTD XHTML 1.1//EN" doctype-system="http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd" />

	<xsl:variable name="base_url">
		<xsl:text>/</xsl:text>
		<xsl:value-of select="/page/file/@key" />
		<xsl:text>/</xsl:text>
	</xsl:variable>
	<xsl:variable name="page_url">
		<xsl:value-of select="$base_url" />
		<xsl:value-of select="/page/page-title-for-url" />
	</xsl:variable>
	<xsl:variable name="page_title">
		<xsl:choose>
			<xsl:when test="not(/page/page-title)" />
			<xsl:when test="/page/page-title!=''">
				<xsl:value-of select="/page/file/records/record/wiki/title" />
			</xsl:when>
			<xsl:otherwise>
				<xsl:text>StartPage</xsl:text>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:variable>

	<xsl:template match="/">
		<html>
			<head>
				<title>
					<xsl:call-template name="_title" />
				</title>
				<link type="text/css" rel="stylesheet" href="/css/wiki.css" />
				<xsl:call-template name="_css" />
				<xsl:call-template name="_js" />
			</head>
			<body>
				<div id="header">
					<h1>
						<xsl:value-of select="page/file/title" />
					</h1>
					<div id="header-menu">
						<ul>
							<li>
								<xsl:element name="a">
									<xsl:attribute name="href">
										<xsl:value-of select="$base_url" />
									</xsl:attribute>
									<xsl:text>トップ</xsl:text>
								</xsl:element>
							</li>
							<xsl:if test="page/page-title">
								<li>
									<xsl:element name="a">
										<xsl:attribute name="href">
											<xsl:value-of select="$page_url" />
											<xsl:text>?edit</xsl:text>
										</xsl:attribute>
										<xsl:text>編集</xsl:text>
									</xsl:element>
								</li>
								<li>
									<xsl:element name="a">
										<xsl:attribute name="href">
											<xsl:value-of select="$page_url" />
											<xsl:text>?history</xsl:text>
										</xsl:attribute>
										<xsl:text>編集履歴</xsl:text>
									</xsl:element>
								</li>
							</xsl:if>
							<li class="tail">
								<xsl:element name="a">
									<xsl:attribute name="href">
										<xsl:value-of select="$base_url" />
										<xsl:text>TitleIndex</xsl:text>
									</xsl:attribute>
									<xsl:text>一覧</xsl:text>
								</xsl:element>
							</li>
							<!--<li>
								<xsl:element name="a">
									<xsl:attribute name="href">
										<xsl:value-of select="$base_url" />
										<xsl:text>History</xsl:text>
									</xsl:attribute>
									<xsl:text>全体の編集履歴</xsl:text>
								</xsl:element>
							</li>-->
							<li class="right">
								<a href="/">p2pncsのトップに戻る</a>
							</li>
						</ul>
					</div>
				</div>
				<div id="contents">
					<xsl:apply-templates select="page/file/wiki" />
				</div>
				<div id="footer">
					<xsl:if test="count(/page/file/records/record)=1">
						<div id="footer-last-editor">
							<xsl:text>最終編集: </xsl:text>
							<xsl:value-of select="/page/file/records/record/wiki/name" />
							<xsl:text> (</xsl:text>
							<xsl:value-of select="/page/file/records/record/@created" />
							<xsl:text>)</xsl:text>
						</div>
					</xsl:if>
					<div id="footer-id">
						<xsl:text>ID: </xsl:text>
						<xsl:value-of select="/page/file/@key" />
					</div>
					<div id="footer-hash">
						<xsl:text>Hash: </xsl:text>
						<xsl:value-of select="/page/file/@recordset" />
					</div>
				</div>
			</body>
		</html>
	</xsl:template>

	<xsl:template name="_title" />
	<xsl:template name="_css" />
	<xsl:template name="_js" />

</xsl:stylesheet>