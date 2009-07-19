<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="wiki_base.xsl" />
	<xsl:import href="wiki_view.xsl" />

	<xsl:template name="_title">
		<xsl:text>TitleIndex :: </xsl:text>
		<xsl:value-of select="/page/file/title" />
		<xsl:text> :: wiki :: p2pncs</xsl:text>
	</xsl:template>

	<xsl:template match="wiki">
		<ul>
			<xsl:for-each select="../records/record">
				<xsl:sort select="wiki/title" />
				<li>
					<xsl:element name="a">
						<xsl:attribute name="href">
							<xsl:value-of select="$base_url" />
							<xsl:value-of select="wiki/title-for-url" />
						</xsl:attribute>
						<xsl:text>/</xsl:text>
						<xsl:value-of select="wiki/title" />
					</xsl:element>
				</li>
			</xsl:for-each>
		</ul>
	</xsl:template>

</xsl:stylesheet>
