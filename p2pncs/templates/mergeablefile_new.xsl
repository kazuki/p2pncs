<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">

	<xsl:template name="create_authserver_form">
		<xsl:call-template name="create_textarea_with_validation">
			<xsl:with-param name="cols">70</xsl:with-param>
			<xsl:with-param name="rows">6</xsl:with-param>
			<xsl:with-param name="name">auth</xsl:with-param>
		</xsl:call-template>
		<xsl:if test="not($confirm_mode or $success_mode)">
			<p>
				<xsl:text>認証サーバは各行に1つずつ、合計で0〜4個程度指定できます。</xsl:text>
				<br/>
				<xsl:text>(多く指定しすぎるとエラーになります)</xsl:text>
				<br/>
				<xsl:text>また、設定しない場合は認証サーバ無しになり、機械が大量の投稿を行える状態になります。</xsl:text>
			</p>
		</xsl:if>
		<p>
			<xsl:text>指定する情報がよくわからない場合や、認証サーバの設置方法に関しては</xsl:text>
			<a href="http://kserver.panicode.com/software/p2pncs/auth-servers">ここ</a>
			<xsl:text>を参照してください。</xsl:text>
		</p>
	</xsl:template>

	<xsl:template match="validation/error[@type='header-size-over']">
		<p>
			<xsl:text>ヘッダのサイズが</xsl:text>
			<xsl:value-of select="@limit" />
			<xsl:text>バイトを超えています。認証サーバ情報や各種ヘッダ情報を減らしてください。</xsl:text>
		</p>
	</xsl:template>

	<xsl:template match="validation/error[@type='record-size-over']">
		<p>
			<xsl:text>最初の投稿のサイズが</xsl:text>
			<xsl:value-of select="@limit" />
			<xsl:text>バイトを超えています。名前や本文を減らしてください。</xsl:text>
		</p>
	</xsl:template>

</xsl:stylesheet>