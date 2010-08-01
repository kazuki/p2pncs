<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="wiki_base.xsl" />
	<xsl:import href="wiki_view.xsl" />

	<xsl:template name="_title">
		<xsl:value-of select="$page_title" />		
		<xsl:text> :: </xsl:text>
		<xsl:value-of select="/page/file/title" />
		<xsl:text> :: wiki :: p2pncs</xsl:text>
	</xsl:template>

	<xsl:template match="wiki">
		<xsl:apply-templates select="../records/record" />
		<xsl:if test="count(../records/record) = 0">
			<p>
				<xsl:text>このページはまだ作成されていないか、データのダウンロードを終えていません。</xsl:text>
				<br/>
				<xsl:text>編集ボタンを押して、ページを作成するか、しばらく時間をおいてからリロードしてみてください。</xsl:text>
			</p>
		</xsl:if>
	</xsl:template>

</xsl:stylesheet>
