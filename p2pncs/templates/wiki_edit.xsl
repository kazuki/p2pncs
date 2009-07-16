<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="wiki_base.xsl" />
	<xsl:import href="wiki_view.xsl" />

	<xsl:template name="_title">
		<xsl:if test="/page/page-title=''">
			<xsl:text>StartPage</xsl:text>
		</xsl:if>
		<xsl:if test="/page/page-title!=''">
			<xsl:value-of select="/page/page-title" />
		</xsl:if>
		<xsl:text>を編集中 :: </xsl:text>
		<xsl:value-of select="/page/file/wiki/title" />
		<xsl:text> :: wiki :: p2pncs</xsl:text>
	</xsl:template>

	<xsl:template match="wiki">
		<xsl:if test="/page/@state='preview'">
			<div id="preview-container">
				<xsl:apply-templates select="/page/file/records/record" />
			</div>
		</xsl:if>
		<xsl:element name="form">
			<xsl:attribute name="id">wiki-editform</xsl:attribute>
			<xsl:attribute name="method">post</xsl:attribute>
			<xsl:attribute name="action">
				<xsl:text>/wiki/</xsl:text>
				<xsl:value-of select="/page/file/@key" />
				<xsl:text>/</xsl:text>
				<xsl:value-of select="/page/page-title" />
				<xsl:text>?edit</xsl:text>
			</xsl:attribute>
			<ul>
				<li>
					<xsl:text>名前: </xsl:text>
					<xsl:element name="input">
						<xsl:attribute name="size">40</xsl:attribute>
						<xsl:attribute name="name">name</xsl:attribute>
						<xsl:attribute name="value">
							<xsl:value-of select="../records/record/wiki/name" />
						</xsl:attribute>
					</xsl:element>
				</li>
			</ul>
			<div>
				<textarea rows="20" cols="80" name="body">
					<xsl:value-of select="../records/record/wiki/raw-body" />
				</textarea>
			</div>
			<div>
				<input type="submit" name="preview" value="プレビュー" />
				<input type="submit" name="update" value="変更" />
			</div>
		</xsl:element>
	</xsl:template>

</xsl:stylesheet>
