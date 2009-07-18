<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">開く :: BBS :: p2pncs</xsl:template>
 
	<xsl:template match="/page">
		<h1>掲示板を開く</h1>
		<form method="post" action="/bbs/open">
			<div>
				<xsl:text>掲示板ID: </xsl:text>
				<input type="text" size="60" name="bbsid" />
				<input type="submit" value="開く"/>
			</div>
		</form>
	</xsl:template>
</xsl:stylesheet>
