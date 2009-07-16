<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />
	<xsl:import href="validation.xsl" />

	<xsl:template name="_title">新規作成 :: Wiki :: p2pncs</xsl:template>
	<xsl:template name="_css">
		<xsl:call-template name="create_validation_stylesheet" />
		<link type="text/css" rel="stylesheet" href="/css/bbs_new.css" />
	</xsl:template>
 
	<xsl:template match="/page">
		<xsl:variable name="confirm_mode" select="validation/data[@name='state']/value = 'confirm'" />
		<xsl:variable name="success_mode" select="validation/data[@name='state']/value = 'success'" />
		<h1>Wikiの新規作成</h1>
		<xsl:if test="$confirm_mode">
			<p>以下の内容でWikiを作成してもよろしいですか？</p>
		</xsl:if>
		<xsl:if test="$success_mode">
			<p>以下の内容でWikiを作成しました。</p>
			<p>
				<xsl:element name="a">
					<xsl:attribute name="href">
						<xsl:text>/wiki/</xsl:text>
						<xsl:value-of select="created/@key" />
					</xsl:attribute>
					<xsl:text>このリンク</xsl:text>
				</xsl:element>
				<xsl:text>をクリックすると作成したWikiへ移動します。</xsl:text>
			</p>
		</xsl:if>
		<form method="post" action="/wiki/new">
			<xsl:call-template name="check_validation" />
			<table>
				<tr>
					<td class="header">タイトル: </td>
					<td>
						<xsl:call-template name="create_input_with_validation">
							<xsl:with-param name="size">70</xsl:with-param>
							<xsl:with-param name="name">title</xsl:with-param>
						</xsl:call-template>
					</td>
				</tr>
				<tr>
					<td class="header">認証サーバ: </td>
					<td>
						<xsl:call-template name="create_textarea_with_validation">
							<xsl:with-param name="cols">70</xsl:with-param>
							<xsl:with-param name="rows">6</xsl:with-param>
							<xsl:with-param name="name">auth</xsl:with-param>
						</xsl:call-template>
						<xsl:if test="not($confirm_mode or $success_mode)">
							<div>
								<xsl:text>認証サーバは0〜4個程度指定できます。</xsl:text>
								<br/>
								<xsl:text>(多く指定しすぎるとエラーになります)</xsl:text>
								<br/>
								<xsl:text>また、設定しない場合は認証サーバ無しになり、機械が大量の投稿を行える状態になります。</xsl:text>
							</div>
						</xsl:if>
					</td>
				</tr>
				<tr>
					<td colspan="2"><hr /></td>
				</tr>
				<tr>
					<td colspan="2" align="center">
						<xsl:if test="$confirm_mode">
							<input type="submit" value="再編集" name="re-edit" />
						</xsl:if>
						<xsl:if test="not($success_mode)">
							<input type="submit" value="作成" />
						</xsl:if>
					</td>
				</tr>
			</table>
		</form>
	</xsl:template>
</xsl:stylesheet>
