<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="wiki_base.xsl" />
	<xsl:import href="wiki_view.xsl" />

	<xsl:template name="_title">
		<xsl:value-of select="$page_title" />
		<xsl:text>を編集中 :: </xsl:text>
		<xsl:value-of select="/page/file/wiki/title" />
		<xsl:text> :: wiki :: p2pncs</xsl:text>
	</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/ui-lightness/jquery-ui-1.7.2.custom.css" />
	</xsl:template>
	<xsl:template name="_js">
		<script type="text/javascript" charset="utf-8" src="/js/jquery-1.3.2.min.js" />
		<script type="text/javascript" charset="utf-8" src="/js/jquery-ui-1.7.2.custom.min.js" />
		<script type="text/javascript" charset="utf-8" src="/js/jquery-simple-dom.js" />
		<script type="text/javascript" charset="utf-8" src="/js/post.js" />
		<script type="text/javascript" charset="utf-8" src="/js/wiki.js" />
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
						<xsl:attribute name="id">postName</xsl:attribute>
						<xsl:attribute name="value">
							<xsl:value-of select="../records/record/wiki/name" />
						</xsl:attribute>
					</xsl:element>
				</li>
			</ul>
			<div>
				<textarea rows="20" cols="80" name="body" id="postBody">
					<xsl:value-of select="../records/record/wiki/raw-body" />
				</textarea>
			</div>
			<div>
				<xsl:if test="count(/page/file/auth-servers/auth-server) &gt; 0">
					<div style="font-size: x-small; display: inline;">
						<xsl:text>認証サーバ: </xsl:text>
						<select style="font-size: x-small" id="authsvr">
							<xsl:for-each select="/page/file/auth-servers/auth-server">
								<xsl:element name="option">
									<xsl:attribute name="value">
										<xsl:value-of select="@index" />
									</xsl:attribute>
									<xsl:value-of select="public-key" />
								</xsl:element>
							</xsl:for-each>
						</select>
					</div>
					</xsl:if>
				<input type="submit" name="preview" value="プレビュー" />
				<button id="postButton">変更</button>

				<xsl:element name="input">
					<xsl:attribute name="id">postKey</xsl:attribute>
					<xsl:attribute name="type">hidden</xsl:attribute>
					<xsl:attribute name="value">
						<xsl:value-of select="/page/file/@key"/>
					</xsl:attribute>
				</xsl:element>
				<xsl:element name="input">
					<xsl:attribute name="id">postPage</xsl:attribute>
					<xsl:attribute name="type">hidden</xsl:attribute>
					<xsl:attribute name="value">
						<xsl:value-of select="/page/page-title" />
					</xsl:attribute>
				</xsl:element>
			</div>
		</xsl:element>
	</xsl:template>

</xsl:stylesheet>
