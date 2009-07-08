<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">初期ノードに接続 :: p2pncs</xsl:template>
 
	<xsl:template match="/page">
		<h1>初期ノードに接続</h1>
		<xsl:if test="count(connected/endpoint) &gt; 0">
			<div style="margin-bottom: 1em">
				<xsl:text>以下の</xsl:text>
				<xsl:value-of select="count(connected/endpoint)" />
				<xsl:text>ノードへの接続を試行しました</xsl:text>
				<xsl:for-each select="connected/endpoint">
					<div>
						<xsl:value-of select="text()" />
					</div>
				</xsl:for-each>
			</div>
		</xsl:if>
		<form method="post" action="/net/init" accept-charset="UTF-8" >
			<div><textarea cols="30" rows="5" name="nodes" /></div>
			<div><input type="submit" value="接続" /></div>
		</form>
	</xsl:template>
</xsl:stylesheet>
