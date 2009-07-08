<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">

	<xsl:template name="create_validation_stylesheet">
		<link type="text/css" rel="stylesheet" href="/css/validation.css" />
	</xsl:template>

	<xsl:template name="check_validation">
		<xsl:call-template name="create_input_with_validation">
			<xsl:with-param name="type">hidden</xsl:with-param>
			<xsl:with-param name="name">state</xsl:with-param>
			<xsl:with-param name="class">validation-hidden</xsl:with-param>
		</xsl:call-template>

		<xsl:if test="validation/all">
			<div class="validation-all-error">
				<xsl:value-of select="validation/all" />
			</div>
		</xsl:if>
	</xsl:template>

	<xsl:template name="create_input_with_validation">
		<xsl:param name="type">text</xsl:param>
		<xsl:param name="name" />
		<xsl:param name="id" />
		<xsl:param name="class" />
		<xsl:param name="size" />
		<xsl:param name="rows" />
		<xsl:param name="cols" />
		<xsl:variable name="x" select="validation/data[@name=$name]" />
		<xsl:variable name="readonly_mode" select="validation/data[@name='state']/value = 'confirm' or validation/data[@name='state']/value = 'success'" />
		<xsl:element name="input">
			<xsl:attribute name="type">
				<xsl:value-of select="$type" />
			</xsl:attribute>
			<xsl:attribute name="name">
				<xsl:value-of select="$name" />
			</xsl:attribute>
			<xsl:attribute name="value">
				<xsl:value-of select="$x/value" />
			</xsl:attribute>
			<xsl:if test="$id">
				<xsl:attribute name="id">
					<xsl:value-of select="$id" />
				</xsl:attribute>
			</xsl:if>
			<xsl:if test="$readonly_mode">
				<xsl:attribute name="style">
					<xsl:text>display:none</xsl:text>
				</xsl:attribute>
			</xsl:if>
			<xsl:if test="$class or $x">
				<xsl:attribute name="class">
					<xsl:value-of select="$class" />
					<xsl:if test="$x and $x/@status='error'">
						<xsl:text> validation-error</xsl:text>
					</xsl:if>
				</xsl:attribute>
			</xsl:if>
			<xsl:if test="$size">
				<xsl:attribute name="size">
					<xsl:value-of select="$size" />
				</xsl:attribute>
			</xsl:if>
		</xsl:element>
		<xsl:if test="$x and $x/@status='error' and $x/msg">
			<div class="validation-error">
				<xsl:value-of select="$x/msg" />
			</div>
		</xsl:if>
		<xsl:if test="$readonly_mode and $type != 'hidden'">
			<div class="validation-confirm">
				<xsl:value-of select="$x/value" />
			</div>
		</xsl:if>
	</xsl:template>

	<xsl:template name="create_textarea_with_validation">
		<xsl:param name="name" />
		<xsl:param name="id" />
		<xsl:param name="class" />
		<xsl:param name="rows" />
		<xsl:param name="cols" />
		<xsl:variable name="x" select="validation/data[@name=$name]" />
		<xsl:variable name="readonly_mode" select="validation/data[@name='state']/value = 'confirm' or validation/data[@name='state']/value = 'success'" />
		<xsl:element name="textarea">
			<xsl:attribute name="name">
				<xsl:value-of select="$name" />
			</xsl:attribute>
			<xsl:if test="$id">
				<xsl:attribute name="id">
					<xsl:value-of select="$id" />
				</xsl:attribute>
			</xsl:if>
			<xsl:if test="$readonly_mode">
				<xsl:attribute name="style">
					<xsl:text>display:none</xsl:text>
				</xsl:attribute>
			</xsl:if>
			<xsl:if test="$class or $x">
				<xsl:attribute name="class">
					<xsl:value-of select="$class" />
					<xsl:if test="$x and $x/@status='error'">
						<xsl:text> validation-error</xsl:text>
					</xsl:if>
				</xsl:attribute>
			</xsl:if>
			<xsl:if test="$rows">
				<xsl:attribute name="rows">
					<xsl:value-of select="$rows" />
				</xsl:attribute>
			</xsl:if>
			<xsl:if test="$cols">
				<xsl:attribute name="cols">
					<xsl:value-of select="$cols" />
				</xsl:attribute>
			</xsl:if>
			<xsl:value-of select="$x/value" />
		</xsl:element>
		<xsl:if test="$x and $x/@status='error' and $x/msg">
			<div class="validation-error">
				<xsl:value-of select="$x/msg" />
			</div>
		</xsl:if>
		<xsl:if test="$readonly_mode">
			<div class="validation-confirm">
				<xsl:value-of select="$x/value" />
			</div>
		</xsl:if>
	</xsl:template>

</xsl:stylesheet>