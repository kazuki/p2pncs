<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">

	<xsl:template match="record">
		<xsl:variable name="type" select="wiki/@markup-type" />
		<xsl:choose>
			<xsl:when test="$type = 'PukiWiki'">
				<xsl:call-template name="pukiwiki_view">
					<xsl:with-param name="body" select="wiki/body" />
				</xsl:call-template>
			</xsl:when>
			<xsl:otherwise>
				<xsl:call-template name="plaintext_view">
					<xsl:with-param name="body" select="wiki/body" />
				</xsl:call-template>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>

	<xsl:template name="plaintext_view">
		<xsl:param name="body" />

		<div id="wiki-view-plaintext">
			<xsl:value-of select="$body" />
		</div>
	</xsl:template>

	<xsl:template name="pukiwiki_view">
		<xsl:param name="body" />

		<div id="wiki-view-pukiwiki">
			<xsl:value-of select="$body" disable-output-escaping="yes" />
		</div>
	</xsl:template>

</xsl:stylesheet>
