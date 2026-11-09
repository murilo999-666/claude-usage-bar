//go:build !windows

// O app de abas só existe no Windows; aqui tudo é vazio.
package main

import "io"

func appSupported() bool                       { return false }
func installApp(_ *texts, _ io.Writer) error   { return nil }
func uninstallApp(_ *texts, _ io.Writer) error { return nil }
func saveLauncherState(_ *Input)               {}
