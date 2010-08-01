<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:output method="xml" encoding="utf-8" doctype-public="-//W3C//DTD XHTML 1.1//EN" doctype-system="http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd" />

	<xsl:template match="/">
		<html>
			<head>
				<title>
					<xsl:value-of select="/page/file/title" />
					<xsl:text> :: BBS :: p2pncs</xsl:text>
				</title>
				<link type="text/css" rel="stylesheet" href="/css/ui-lightness/jquery-ui-1.7.2.custom.css" />
				<script type="text/javascript" charset="utf-8" src="/js/jquery-1.3.2.min.js" />
				<script type="text/javascript" charset="utf-8" src="/js/jquery-ui-1.7.2.custom.min.js" />
				<script type="text/javascript" charset="utf-8" src="/js/jquery-simple-dom.js" />
				<script type="text/javascript" charset="utf-8" src="/js/post.js" />
				<script type="text/javascript" charset="utf-8" src="/js/bbs.js" />
				<link type="text/css" rel="stylesheet" href="/css/bbs.css" />
			</head>
			<body>
				<h1><xsl:value-of select="/page/file/title" /></h1>
				<div id="bbsinfo">
					<div>
						<xsl:text>ID: </xsl:text>
						<xsl:value-of select="/page/file/@key" />
					</div>
					<div>
						<xsl:text>ハッシュ: </xsl:text>
						<xsl:value-of select="/page/file/@recordset" />
					</div>
					<div>
						<xsl:text>作成日時: </xsl:text>
						<xsl:value-of select="/page/file/@created" />
						<xsl:text>, 管理更新日時: </xsl:text>
						<xsl:value-of select="/page/file/@lastManaged" />
					</div>
				</div>
				<div id="bbsNavi">
					<a href="/">トップページに戻る</a>
					<xsl:text>, </xsl:text>
					<a href="/list">一覧に戻る</a>
				</div>
				<div id="bbsBody">
					<xsl:for-each select="/page/file/records/record">
						<xsl:sort select="@created" order="ascending" />
						<xsl:element name="div">
							<xsl:attribute name="class">
								<xsl:text>bbsPost </xsl:text>
								<xsl:if test="bbs/@is-new='true'">
									<xsl:text>unread </xsl:text>
								</xsl:if>
							</xsl:attribute>
							<div class="postName">
								<xsl:text>[</xsl:text>
								<span class="postShortId">
									<xsl:value-of select="bbs/@short-id"/>
								</span>
								<xsl:text>] </xsl:text>
								<xsl:value-of select="bbs/name" />
								<xsl:text>: </xsl:text>
								<xsl:value-of select="@created" />
							</div>
							<div class="postBody">
								<xsl:value-of select="bbs/body" />
							</div>
						</xsl:element>
					</xsl:for-each>
				</div>
				<hr />
				<div id="postForm">
					<xsl:element name="input">
						<xsl:attribute name="id">postKey</xsl:attribute>
						<xsl:attribute name="type">hidden</xsl:attribute>
						<xsl:attribute name="value">
							<xsl:value-of select="/page/file/@key"/>
						</xsl:attribute>
					</xsl:element>
					<div>
						<xsl:text>名前: </xsl:text>
						<input type="text" id="postName" />
						<button id="postButton">投稿</button>
					</div>
					<div>
						<textarea cols="50" rows="8" id="postBody"></textarea>
					</div>
					<xsl:if test="count(/page/file/auth-servers/auth-server) &gt; 0">
						<div style="font-size: x-small">
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
				</div>
			</body>
		</html>
	</xsl:template>
</xsl:stylesheet>
